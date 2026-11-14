#!/usr/bin/env python3
"""
VANTAGE robot adapter (ROS 2 <-> MQTT, contract vantage/v1).

One node per robot. It
  * publishes the factsheet (class, footprint, priority) and ONLINE/OFFLINE state,
  * publishes the candidate path:
      - path_source=planner : Nav2 global plan (/plan); a new revision is sent when the
                              plan geometry changes by more than `replan_tolerance`,
      - path_source=route   : fixed virtual-line route given as parameters (AGV),
  * reports state at `state_rate` Hz: pose from TF map->base_link and arc-length progress,
  * enforces clearance: the robot may not drive its reference point past `limitS`
    of the active revision. When the remaining distance is below the stopping
    distance, a zero Twist is published on `hold_topic` (feed it into twist_mux
    with a higher priority than Nav2's cmd_vel).

Requires: rclpy, tf2_ros, paho-mqtt (1.6 or 2.x).
"""
import json
import math
import time

import paho.mqtt.client as mqtt
import rclpy
from geometry_msgs.msg import Twist
from nav_msgs.msg import Odometry, Path
from rclpy.node import Node
from tf2_ros import Buffer, TransformException, TransformListener

ROOT = "vantage/v1"


def yaw_of(q):
    return math.atan2(2.0 * (q.w * q.z + q.x * q.y), 1.0 - 2.0 * (q.y * q.y + q.z * q.z))


class VantageAdapter(Node):
    def __init__(self):
        super().__init__("vantage_adapter")
        p = self.declare_parameter
        self.robot_id = p("robot_id", "AMR-01").value
        self.vehicle_class = p("vehicle_class", "AMR").value          # AMR | AGV
        self.path_source = p("path_source", "planner").value          # planner | route
        self.mission_id = p("mission_id", "M1").value
        self.priority = int(p("priority", 2).value)
        self.footprint = [float(v) for v in p("footprint", [0.4, 0.4, 0.275, 0.275]).value]  # front, rear, left, right
        self.route_xy = [float(v) for v in p("route_xy", [0.0]).value]  # x0,y0,x1,y1,... for path_source=route
        self.route_nodes = list(p("route_nodes", [""]).value)
        self.broker = p("broker_host", "192.168.137.1").value
        self.port = int(p("broker_port", 1883).value)
        self.map_frame = p("map_frame", "map").value
        self.base_frame = p("base_frame", "base_link").value
        self.state_rate = float(p("state_rate", 10.0).value)
        self.decel = float(p("deceleration", 0.5).value)
        self.stop_buffer = float(p("stop_buffer", 0.05).value)
        self.replan_tol = float(p("replan_tolerance", 0.15).value)
        self.hold_topic = p("hold_topic", "/cmd_vel_fmr_hold").value

        self.revision = -1
        self.poses = []          # [(x, y, theta)]
        self.cum = []            # cumulative arc length
        self.progress = 0.0
        self.progress_idx = 0
        self.limit = 0.0
        self.last_decision = 0
        self.header_id = 0
        self.speed = 0.0
        self.holding = False

        self.tf = Buffer()
        self.tf_listener = TransformListener(self.tf, self)
        self.hold_pub = self.create_publisher(Twist, self.hold_topic, 10)
        self.create_subscription(Odometry, "/odom", self.on_odom, 10)
        if self.path_source == "planner":
            self.create_subscription(Path, "/plan", self.on_plan, 10)

        self.mqtt = self.make_client()
        self.mqtt.will_set(self.topic("connection"), json.dumps({"header": self.header(), "connectionState": "CONNECTIONBROKEN"}), qos=1, retain=True)
        self.mqtt.on_connect = self.on_connect
        self.mqtt.on_message = self.on_message
        self.mqtt.connect_async(self.broker, self.port, keepalive=10)
        self.mqtt.loop_start()

        self.create_timer(1.0 / self.state_rate, self.tick)
        self.create_timer(0.05, self.enforce)
        if self.path_source == "route":
            self.create_timer(1.0, self.publish_route_once)

    # ───────────── MQTT ─────────────
    def make_client(self):
        cid = f"vantage-adapter-{self.robot_id}"
        try:
            return mqtt.Client(mqtt.CallbackAPIVersion.VERSION1, client_id=cid)
        except AttributeError:
            return mqtt.Client(client_id=cid)

    def topic(self, leaf):
        return f"{ROOT}/{self.robot_id}/{leaf}"

    def header(self):
        self.header_id += 1
        return {"headerId": self.header_id, "timestamp": time.strftime("%Y-%m-%dT%H:%M:%S", time.gmtime()) + "Z",
                "version": "1.0.0", "manufacturer": "POLMAN", "serialNumber": self.robot_id, "t": time.time()}

    def send(self, leaf, body, retain=False):
        body = dict(body)
        body["header"] = self.header()
        self.mqtt.publish(self.topic(leaf), json.dumps(body), qos=1, retain=retain)

    def on_connect(self, client, userdata, flags, rc):
        self.get_logger().info(f"MQTT connected to {self.broker}:{self.port}")
        client.subscribe(self.topic("clearance"), qos=1)
        f, r, l, rt = self.footprint
        self.send("factsheet", {"vehicleClass": self.vehicle_class, "displayName": self.robot_id,
                                "footprint": {"front": f, "rear": r, "left": l, "right": rt},
                                "priority": self.priority}, retain=True)
        self.send("connection", {"connectionState": "ONLINE"}, retain=True)
        if self.poses:
            self.publish_path(self.poses, self.revision, resend=True)

    def on_message(self, client, userdata, msg):
        try:
            m = json.loads(msg.payload.decode("utf-8"))
        except ValueError:
            return
        # Only decisions for the active revision and newer than the last applied one.
        if m.get("missionId") != self.mission_id or m.get("revision") != self.revision:
            return
        if m.get("decisionNo", 0) <= self.last_decision:
            return
        self.last_decision = m["decisionNo"]
        self.limit = float(m.get("limitS", 0.0))
        self.get_logger().info(f"#{self.last_decision} {m.get('status')} limit={self.limit:.2f} m ({m.get('reason', '')})")

    # ───────────── path input ─────────────
    def publish_route_once(self):
        if self.revision >= 0 or len(self.route_xy) < 4:
            return
        pts = [(self.route_xy[i], self.route_xy[i + 1]) for i in range(0, len(self.route_xy) - 1, 2)]
        poses = []
        for i, (x, y) in enumerate(pts):
            nx, ny = pts[min(i + 1, len(pts) - 1)]
            px, py = pts[max(i - 1, 0)]
            th = math.atan2(ny - y, nx - x) if i < len(pts) - 1 else math.atan2(y - py, x - px)
            poses.append((x, y, th))
        self.publish_path(poses, 0)

    def on_plan(self, msg: Path):
        pts = []
        for ps in msg.poses:
            x, y = ps.pose.position.x, ps.pose.position.y
            if pts and math.hypot(x - pts[-1][0], y - pts[-1][1]) < 0.05:
                continue
            pts.append((x, y, yaw_of(ps.pose.orientation)))
        if len(pts) < 2:
            return
        if self.poses and self.max_deviation(pts) < self.replan_tol:
            return  # same geometry: no new revision
        self.publish_path(pts, self.revision + 1)

    def max_deviation(self, pts):
        worst = 0.0
        for (x, y, _) in pts[::5]:
            worst = max(worst, min(math.hypot(x - a, y - b) for (a, b, _) in self.poses))
        return worst

    def publish_path(self, poses, revision, resend=False):
        if not resend:
            self.revision = revision
            self.poses = poses
            self.cum = [0.0]
            for i in range(1, len(poses)):
                self.cum.append(self.cum[-1] + math.hypot(poses[i][0] - poses[i - 1][0], poses[i][1] - poses[i - 1][1]))
            self.progress, self.progress_idx, self.limit, self.last_decision = 0.0, 0, 0.0, 0
        body = {"missionId": self.mission_id, "revision": self.revision,
                "source": "AgvVirtualLine" if self.path_source == "route" else "AmrPlanner",
                "poses": [{"x": x, "y": y, "theta": th} for (x, y, th) in self.poses]}
        if self.path_source == "route":
            for i, node in enumerate(self.route_nodes[: len(self.poses)]):
                if node:
                    body["poses"][i]["nodeId"] = node
        self.send("path", body)
        self.get_logger().info(f"path r{self.revision}: {len(self.poses)} poses, {self.cum[-1]:.2f} m")

    # ───────────── state / enforcement ─────────────
    def on_odom(self, msg: Odometry):
        self.speed = abs(msg.twist.twist.linear.x)

    def pose(self):
        try:
            t = self.tf.lookup_transform(self.map_frame, self.base_frame, rclpy.time.Time())
        except TransformException:
            return None
        tr = t.transform
        return tr.translation.x, tr.translation.y, yaw_of(tr.rotation)

    def update_progress(self, x, y):
        """Project the pose on the path near the last index (monotonic)."""
        best, best_s = float("inf"), self.progress
        lo, hi = max(0, self.progress_idx - 2), min(len(self.poses) - 1, self.progress_idx + 40)
        for i in range(lo, hi):
            ax, ay, _ = self.poses[i]
            bx, by, _ = self.poses[i + 1]
            dx, dy = bx - ax, by - ay
            seg = dx * dx + dy * dy
            u = 0.0 if seg == 0 else max(0.0, min(1.0, ((x - ax) * dx + (y - ay) * dy) / seg))
            d = math.hypot(ax + u * dx - x, ay + u * dy - y)
            if d < best:
                best, best_s, self.progress_idx = d, self.cum[i] + u * math.sqrt(seg), i
        self.progress = max(self.progress, best_s)

    def tick(self):
        p = self.pose()
        if p is None or self.revision < 0:
            return
        self.update_progress(p[0], p[1])
        self.send("state", {"missionId": self.mission_id, "revision": self.revision,
                            "agvPosition": {"x": p[0], "y": p[1], "theta": p[2], "mapId": self.map_frame, "positionInitialized": True},
                            "velocity": {"vx": self.speed, "vy": 0.0, "omega": 0.0},
                            "pathProgress": self.progress, "driving": self.speed > 0.01,
                            "operatingMode": "AUTOMATIC", "errors": ["CLEARANCE_HOLD"] if self.holding else []})

    def enforce(self):
        if self.revision < 0:
            return
        remaining = self.limit - self.progress
        stopping = self.speed * self.speed / (2.0 * self.decel) + self.stop_buffer
        at_goal = self.cum and self.limit >= self.cum[-1] - 1e-3
        self.holding = (not at_goal) and remaining <= stopping
        if self.holding:
            self.hold_pub.publish(Twist())


def main():
    rclpy.init()
    node = VantageAdapter()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        node.send("connection", {"connectionState": "OFFLINE"}, retain=True)
        node.mqtt.loop_stop()
        node.mqtt.disconnect()
        node.destroy_node()
        rclpy.shutdown()


if __name__ == "__main__":
    main()

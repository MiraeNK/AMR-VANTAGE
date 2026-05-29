#!/usr/bin/env python3
import rclpy
from rclpy.node import Node
import paho.mqtt.client as mqtt
import json
import math
import time
from geometry_msgs.msg import PoseWithCovarianceStamped, PoseStamped
from nav_msgs.msg import Odometry
from sensor_msgs.msg import LaserScan
from action_msgs.msg import GoalStatusArray

class AmrmqttBridge(Node):
    def __init__(self):
        super().__init__('amr_mqtt_bridge')
        
        self.robot_id = "AMR-01"
        self.mqtt_broker = "192.168.137.1"
        self.mqtt_port = 1883
        
        self.client = mqtt.Client(f"{self.robot_id}_bridge")
        self.client.on_connect = self.on_mqtt_connect
        self.client.on_message = self.on_mqtt_message
        
        # Connect MQTT
        self.get_logger().info(f"Connecting to MQTT broker at {self.mqtt_broker}...")
        try:
            self.client.connect(self.mqtt_broker, self.mqtt_port, 60)
            self.client.loop_start()
        except Exception as e:
            self.get_logger().error(f"Failed to connect to MQTT: {e}")
            
        # ROS 2 Publishers
        self.initial_pose_pub = self.create_publisher(PoseWithCovarianceStamped, '/initialpose', 10)
        self.goal_pub = self.create_publisher(PoseStamped, '/goal_pose', 10)
        
        # ROS 2 Subscribers
        self.create_subscription(Odometry, '/odom', self.odom_callback, 10)
        self.create_subscription(LaserScan, '/scan', self.scan_callback, 10)
        self.create_subscription(GoalStatusArray, '/navigate_to_pose/_action/status', self.nav_status_callback, 10)
        
        # Timers
        self.create_timer(1.0, self.publish_identity)
        
    def on_mqtt_connect(self, client, userdata, flags, rc):
        self.get_logger().info("Connected to MQTT Broker!")
        self.client.subscribe(f"amr/{self.robot_id}/cmd/#")
        
    def on_mqtt_message(self, client, userdata, msg):
        topic = msg.topic
        payload_str = msg.payload.decode('utf-8')
        try:
            data = json.loads(payload_str)
        except Exception as e:
            self.get_logger().error(f"Invalid JSON: {e}")
            return
            
        if topic == f"amr/{self.robot_id}/cmd/set_pose":
            self.handle_initial_pose(data)
        elif topic == f"amr/{self.robot_id}/cmd/goal":
            self.handle_nav_goal(data)
            
    def handle_initial_pose(self, data):
        pose_msg = PoseWithCovarianceStamped()
        pose_msg.header.frame_id = "map"
        pose_msg.header.stamp = self.get_clock().now().to_msg()
        
        pose_msg.pose.pose.position.x = float(data.get('x', 0.0))
        pose_msg.pose.pose.position.y = float(data.get('y', 0.0))
        
        yaw = float(data.get('yaw', 0.0))
        pose_msg.pose.pose.orientation.z = math.sin(yaw / 2.0)
        pose_msg.pose.pose.orientation.w = math.cos(yaw / 2.0)
        
        # Default covariance
        pose_msg.pose.covariance[0] = 0.25
        pose_msg.pose.covariance[7] = 0.25
        pose_msg.pose.covariance[35] = 0.06853892
        
        self.initial_pose_pub.publish(pose_msg)
        self.get_logger().info(f"Published /initialpose: x={pose_msg.pose.pose.position.x}, y={pose_msg.pose.pose.position.y}, yaw={yaw}")
        
    def handle_nav_goal(self, data):
        goal_msg = PoseStamped()
        goal_msg.header.frame_id = "map"
        goal_msg.header.stamp = self.get_clock().now().to_msg()
        
        goal_msg.pose.position.x = float(data.get('x', 0.0))
        goal_msg.pose.position.y = float(data.get('y', 0.0))
        
        yaw = float(data.get('yaw', 0.0))
        goal_msg.pose.orientation.z = math.sin(yaw / 2.0)
        goal_msg.pose.orientation.w = math.cos(yaw / 2.0)
        
        self.goal_pub.publish(goal_msg)
        self.get_logger().info(f"Published /goal_pose: x={goal_msg.pose.position.x}, y={goal_msg.pose.position.y}")
        
        # Publish Ack
        task_id = data.get('task_id', '')
        if task_id:
            ack_payload = {
                "task_id": task_id,
                "command": "NavGoal",
                "status": "Accepted",
                "timestamp": int(time.time())
            }
            self.client.publish(f"amr/{self.robot_id}/event/ack", json.dumps(ack_payload))
            
    def publish_identity(self):
        payload = {
            "robot_id": self.robot_id,
            "name": "Polebot 1",
            "online": True
        }
        self.client.publish(f"amr/{self.robot_id}/identity", json.dumps(payload), retain=True)

    def odom_callback(self, msg):
        # In a real scenario, use TF (map -> base_link) instead of /odom for absolute pose.
        # But for this bridge example, we will publish odom pose.
        qx = msg.pose.pose.orientation.x
        qy = msg.pose.pose.orientation.y
        qz = msg.pose.pose.orientation.z
        qw = msg.pose.pose.orientation.w
        
        yaw = math.atan2(2.0 * (qw * qz + qx * qy), 1.0 - 2.0 * (qy * qy + qz * qz))
        
        payload = {
            "x": msg.pose.pose.position.x,
            "y": msg.pose.pose.position.y,
            "yaw": yaw,
            "linear_vel": msg.twist.twist.linear.x,
            "angular_vel": msg.twist.twist.angular.z,
            "timestamp": int(time.time())
        }
        self.client.publish(f"amr/{self.robot_id}/status/pose", json.dumps(payload))
        
    def scan_callback(self, msg):
        # To avoid lagging the UI, you can throttle the scan publish rate here (e.g. 5Hz)
        # Using a simple check to publish every 0.2 seconds
        if not hasattr(self, 'last_scan_time'):
            self.last_scan_time = 0
            
        current_time = time.time()
        if current_time - self.last_scan_time < 0.2:
            return
        self.last_scan_time = current_time
        
        # Handle infinite ranges and compress if needed to reduce network payload
        ranges = [float(r) if not math.isinf(r) and not math.isnan(r) else 0.0 for r in msg.ranges]
        
        payload = {
            "angle_min": msg.angle_min,
            "angle_inc": msg.angle_increment,
            "ranges": ranges,
            "timestamp": int(current_time)
        }
        self.client.publish(f"amr/{self.robot_id}/status/scan", json.dumps(payload))

    def nav_status_callback(self, msg):
        # Simply parsing Nav2 GoalStatus
        # Status 4 = SUCCEEDED
        if not msg.status_list:
            return
            
        latest_status = msg.status_list[-1].status
        if latest_status == 4:
            payload = {
                "event": "arrived",
                "timestamp": int(time.time())
            }
            self.client.publish(f"amr/{self.robot_id}/event/arrived", json.dumps(payload))

def main(args=None):
    rclpy.init(args=args)
    bridge = AmrmqttBridge()
    try:
        rclpy.spin(bridge)
    except KeyboardInterrupt:
        pass
    finally:
        bridge.client.loop_stop()
        bridge.client.disconnect()
        bridge.destroy_node()
        rclpy.shutdown()

if __name__ == '__main__':
    main()

# VANTAGE ROS 2 adapter

`vantage_adapter.py` connects one robot to the VANTAGE FMR over MQTT (contract `vantage/v1`, see `docs/MESSAGE_CONTRACT.md`).

## AMR (Nav2 planner path)

```bash
python3 vantage_adapter.py --ros-args \
  -p robot_id:=AMR-01 -p vehicle_class:=AMR -p path_source:=planner \
  -p footprint:="[0.40, 0.40, 0.275, 0.275]" -p priority:=2 \
  -p broker_host:=192.168.137.1
```

## AGV (fixed virtual line)

```bash
python3 vantage_adapter.py --ros-args \
  -p robot_id:=AGV-01 -p vehicle_class:=AGV -p path_source:=route \
  -p route_xy:="[0.7, 4.2, 13.3, 4.2]" -p route_nodes:="['N10', 'N11']" \
  -p footprint:="[0.5, 0.5, 0.3, 0.3]" -p priority:=1
```

## Enforcing the clearance limit

The adapter publishes a zero `Twist` on `/cmd_vel_fmr_hold` while the robot must hold before an ungranted segment. Give that topic a higher priority than Nav2 in `twist_mux`:

```yaml
twist_mux:
  ros__parameters:
    topics:
      fmr_hold:
        topic: /cmd_vel_fmr_hold
        timeout: 0.2
        priority: 100
      navigation:
        topic: /cmd_vel_nav
        timeout: 0.5
        priority: 10
```

Physical safety (scanner fields, e-stop) stays with the robot; the FMR hold is a coordination layer, not a safety function.

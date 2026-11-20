"""RobotWin Studio ↔ ROS 2.

    ros2 launch robotwin_bridge robotwin_bridge.launch.py example:=car
    ros2 launch robotwin_bridge robotwin_bridge.launch.py world:=/path/robot.rwworld camera:=true rviz:=true
    ros2 launch robotwin_bridge robotwin_bridge.launch.py example:=quadcopter
"""

from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument, OpaqueFunction
from launch.conditions import IfCondition
from launch.substitutions import LaunchConfiguration
from launch_ros.actions import Node
from launch_ros.parameter_descriptions import ParameterValue


def _robot_state_publisher(context, *args, **kwargs):
    if LaunchConfiguration("robot_state_publisher").perform(context).lower() not in ("1", "true", "yes"):
        return []
    from robotwin_bridge.export_urdf import urdf_for
    try:
        urdf = urdf_for(LaunchConfiguration("example").perform(context) or None,
                        LaunchConfiguration("world").perform(context) or None)
    except Exception as exc:  # the bridge still runs without the robot model
        print(f"[robotwin_bridge] URDF export failed, robot_state_publisher not started: {exc}")
        return []
    return [Node(package="robot_state_publisher", executable="robot_state_publisher", output="screen",
                 parameters=[{"robot_description": urdf, "use_sim_time": True}])]


def generate_launch_description():
    args = [
        DeclareLaunchArgument("example", default_value="car", description="car | line-follower | obstacle-avoider | quadcopter"),
        DeclareLaunchArgument("world", default_value="", description=".rwworld or .rwproj file (overrides example)"),
        DeclareLaunchArgument("host", default_value="", description="connect to a running RobotWin API server"),
        DeclareLaunchArgument("port", default_value="5555"),
        DeclareLaunchArgument("seed", default_value="0"),
        DeclareLaunchArgument("realtime", default_value="true"),
        DeclareLaunchArgument("camera", default_value="false"),
        DeclareLaunchArgument("scan", default_value="true"),
        DeclareLaunchArgument("rate", default_value="50.0"),
        DeclareLaunchArgument("robot_state_publisher", default_value="true"),
        DeclareLaunchArgument("rviz", default_value="false"),
    ]
    bridge = Node(
        package="robotwin_bridge", executable="bridge", name="robotwin_bridge", output="screen",
        parameters=[{
            "use_sim_time": True,
            "example": ParameterValue(LaunchConfiguration("example"), value_type=str),
            "world": ParameterValue(LaunchConfiguration("world"), value_type=str),
            "host": ParameterValue(LaunchConfiguration("host"), value_type=str),
            "port": ParameterValue(LaunchConfiguration("port"), value_type=int),
            "seed": ParameterValue(LaunchConfiguration("seed"), value_type=int),
            "realtime": ParameterValue(LaunchConfiguration("realtime"), value_type=bool),
            "camera": ParameterValue(LaunchConfiguration("camera"), value_type=bool),
            "scan": ParameterValue(LaunchConfiguration("scan"), value_type=bool),
            "rate": ParameterValue(LaunchConfiguration("rate"), value_type=float),
        }],
    )
    rviz = Node(package="rviz2", executable="rviz2", output="screen", condition=IfCondition(LaunchConfiguration("rviz")),
                parameters=[{"use_sim_time": True}])
    return LaunchDescription(args + [bridge, OpaqueFunction(function=_robot_state_publisher), rviz])

"""RobotWin Studio automation: headless simulation client, Gymnasium environments, imitation learning.

Türkçe: RobotWin Studio otomasyonu — başsız simülasyon istemcisi, Gymnasium ortamları, taklit öğrenme.
"""

from .client import RobotWinClient, RobotWinError, Server, launch, find_server, decode_image, save_ppm, save_png

__version__ = "1.0.0"

__all__ = ["RobotWinClient", "RobotWinError", "Server", "launch", "find_server", "decode_image", "save_ppm", "save_png",
           "RobotWinEnv", "EnvSpec", "VectorEnv", "DemoRecorder", "load_demos",
           "Experiment", "Factor", "Metric", "ExperimentResult", "run_spec"]


def __getattr__(name):  # lazy: keep "import robotwin" light and free of optional dependencies
    if name in ("RobotWinEnv", "EnvSpec"):
        from . import env
        return getattr(env, name)
    if name == "VectorEnv":
        from .vector import VectorEnv
        return VectorEnv
    if name in ("Experiment", "Factor", "Metric", "ExperimentResult", "run_spec"):
        from . import experiment
        return getattr(experiment, name)
    if name in ("DemoRecorder", "load_demos"):
        from . import imitation
        return getattr(imitation, name)
    raise AttributeError(name)

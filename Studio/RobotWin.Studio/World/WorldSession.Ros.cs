using System;
using System.Threading.Tasks;
using RobotTwin.CoreSim.Ros;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [ros] The running world on ROS 2 through rosbridge (CoreSim/Ros): /clock and /joint_states out, /joint_command in.
    /// The connection is the user's (Ortam → ROS 2); the link follows the runs, built when the physics is.
    /// </summary>
    public sealed partial class WorldSession
    {
        private RosBridgeClient? _ros;
        private RosWorldLink? _rosLink;

        /// <summary>The rosbridge connection, when there is one (open or dropped).</summary>
        public RosBridgeClient? Ros => _ros;

        public RosWorldLink? RosLink => _rosLink;

        /// <summary>Connects to rosbridge at <paramref name="address"/> (ws://host:9090); null on success, else why not.</summary>
        public async Task<string?> ConnectRos(Uri address)
        {
            DisconnectRos();
            var client = new RosBridgeClient(address);
            try
            {
                await client.ConnectAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is System.Net.WebSockets.WebSocketException || ex is OperationCanceledException || ex is System.Net.Http.HttpRequestException || ex is ArgumentException || ex is InvalidOperationException)
            {
                client.Dispose();
                return ex is OperationCanceledException ? "rosbridge 5 saniyede yanıt vermedi." : ex.Message;
            }

            using (SimThread.Hold())
            {
                _ros = client;
                AttachRos();
            }

            return null;
        }

        public void DisconnectRos()
        {
            using (SimThread.Hold())
            {
                _rosLink = null;
                _ros?.Dispose();
                _ros = null;
            }
        }

        /// <summary>A run started (or a connection came while one runs): the link to its physics.</summary>
        private void AttachRos()
        {
            _rosLink = _ros != null && _ros.Connected && Physics != null && World != null ? new RosWorldLink(_ros, World, Physics) : null;
            _rosLink?.AttachSensors(Sensing?.Sensors);   // [ros-sense] the world's LiDARs, IMUs, cameras … (again when sensing starts)
        }

        /// <summary>After each step of the run (simulation thread or the frames, gate held).</summary>
        private void StepRos()
        {
            if (_rosLink != null && Simulator != null) _rosLink.Step(Simulator.Time);
        }
    }
}

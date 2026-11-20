using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace RobotTwin.CoreSim.Automation
{
    /// <summary>
    /// Line-delimited JSON-RPC 2.0 over TCP: each request is one UTF-8 JSON object (or batch array)
    /// terminated by '\n', each response likewise. Binds to 127.0.0.1 unless told otherwise; one
    /// simulation per server (run several processes for parallel environments). Requests from all
    /// clients are executed one at a time, in arrival order.
    /// </summary>
    public sealed class ApiServer : IDisposable
    {
        private readonly ApiSession _session;
        private readonly object _gate = new object();
        private readonly List<TcpClient> _clients = new List<TcpClient>();
        private TcpListener? _listener;
        private Thread? _accept;
        private volatile bool _stopping;

        public ApiServer(ApiSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        /// <summary>Runs a call on the right thread (the app passes its dispatcher); null = the client's thread.</summary>
        public Func<Func<string?>, string?>? Invoker { get; set; }

        public int Port { get; private set; }

        public IPAddress Address { get; private set; } = IPAddress.Loopback;

        /// <summary>Raised when a client connects or disconnects (endpoint, connected).</summary>
        public event Action<string, bool>? ClientChanged;

        /// <summary>Starts listening; port 0 picks a free port (see <see cref="Port"/>).</summary>
        public void Start(int port, IPAddress? address = null)
        {
            Address = address ?? IPAddress.Loopback;
            _listener = new TcpListener(Address, port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _accept = new Thread(AcceptLoop) { IsBackground = true, Name = "RobotWin API accept" };
            _accept.Start();
        }

        private void AcceptLoop()
        {
            while (!_stopping)
            {
                TcpClient client;
                try
                {
                    client = _listener!.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    if (_stopping) return;
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (InvalidOperationException)
                {
                    return;
                }

                client.NoDelay = true;
                lock (_clients) _clients.Add(client);
                var t = new Thread(() => Serve(client)) { IsBackground = true, Name = "RobotWin API client" };
                t.Start();
            }
        }

        private void Serve(TcpClient client)
        {
            string endpoint = client.Client.RemoteEndPoint?.ToString() ?? "?";
            ClientChanged?.Invoke(endpoint, true);
            bool authorized = false;   // the session checks its host token (if any)
            try
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 1 << 16);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1 << 16) { NewLine = "\n", AutoFlush = false };
                while (!_stopping)
                {
                    string? line = reader.ReadLine();
                    if (line == null) break;
                    if (line.Length == 0 || line.Trim().Length == 0) continue;
                    bool auth = authorized;
                    string? response;
                    if (Invoker != null)
                    {
                        response = Invoker(() =>
                        {
                            lock (_gate) return _session.Handle(line, ref auth);
                        });
                    }
                    else
                    {
                        lock (_gate) response = _session.Handle(line, ref auth);
                    }

                    authorized = auth;
                    if (response == null) continue;
                    writer.Write(response);
                    writer.Write('\n');
                    writer.Flush();
                }
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
            finally
            {
                lock (_clients) _clients.Remove(client);
                client.Dispose();
                ClientChanged?.Invoke(endpoint, false);
            }
        }

        public void Stop()
        {
            _stopping = true;
            try
            {
                _listener?.Stop();
            }
            catch (SocketException)
            {
            }

            lock (_clients)
            {
                foreach (var c in _clients) c.Dispose();
                _clients.Clear();
            }
        }

        public void Dispose() => Stop();
    }
}

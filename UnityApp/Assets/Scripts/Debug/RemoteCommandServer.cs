using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RobotTwin.Debugging
{
    public class RemoteCommandServer : MonoBehaviour
    {
        private HttpListener _listener;
        private Thread _listenerThread;
        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
        private bool _isRunning = true;
        private const string Url = "http://localhost:8085/";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            var go = new GameObject("RemoteCommandServer");
            go.AddComponent<RemoteCommandServer>();
            DontDestroyOnLoad(go);
        }

        private void Start()
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(Url);
            _listener.Start();
            Debug.Log($"[RemoteCommandServer] Listening on {Url}");

            _listenerThread = new Thread(ListenLoop);
            _listenerThread.Start();
        }

        private void OnDestroy()
        {
            _isRunning = false;
            _listener?.Stop();
            _listenerThread?.Abort();
        }

        private void Update()
        {
            while (!_mainThreadActions.IsEmpty)
            {
                if (_mainThreadActions.TryDequeue(out var action))
                {
                    action?.Invoke();
                }
            }
        }

        private void ListenLoop()
        {
            while (_isRunning && _listener.IsListening)
            {
                try
                {
                    var context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem((_) => HandleRequest(context));
                }
                catch (HttpListenerException)
                {
                    // Listener stopped
                    break;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[RemoteCommandServer] Error: {e.Message}");
                }
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            string responseString = "OK";
            int statusCode = 200;

            try
            {
                string rawUrl = context.Request.RawUrl; 
                string command = rawUrl.Split('?')[0].Trim('/');
                var queryParams = System.Web.HttpUtility.ParseQueryString(context.Request.Url.Query);

                switch (command)
                {
                    case "screenshot":
                        Enqueue(() =>
                        {
                            string filename = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                             string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
                             if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                             
                             string fullPath = Path.Combine(path, filename);
                             ScreenCapture.CaptureScreenshot(Path.Combine("Screenshots", filename));
                             Debug.Log($"[RemoteCommandServer] Screenshot captured: {fullPath}");
                        });
                        break;

                    case "run-tests":
                         Enqueue(() =>
                         {
                             Debug.Log("[RemoteCommandServer] Triggering Tests...");
                         });
                        break;
                    
                    case "action":
                        string actionType = queryParams["type"];
                        string target = queryParams["target"];
                        Enqueue(() =>
                        {
                            Debug.Log($"[RemoteCommandServer] ACTION: {actionType} on {target}");
                            // TODO: Implement actual UI Toolkit event simulation
                        });
                        break;

                    case "query":
                        string selector = queryParams["target"];
                        Debug.Log($"[RemoteCommandServer] QUERY: {selector}");
                        // Mock Response for now
                        if (selector == "CurrentScene") responseString = "{\"value\": \"CircuitStudio\"}";
                        else if (selector == "#RunMode") responseString = "{\"value\": true}";
                        else responseString = "{\"value\": null}";
                        break;

                    case "reset":
                        Enqueue(() =>
                        {
                            Debug.Log("[RemoteCommandServer] Resetting Scene...");
                            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                        });
                        break;

                    case "status":
                        int engineVer = -1;
                        try { engineVer = RobotTwin.Core.NativeBridge.GetVersion(); } catch {}
                        
                        string engineStatus = (engineVer > 0) ? "connected" : "disconnected";
                        responseString = $"{{\"engine\": \"{engineStatus}\", \"version\": {engineVer}}}";
                        break;

                    default:
                        statusCode = 404;
                        responseString = "Unknown Command";
                        break;
                }
            }
            catch (Exception ex)
            {
                statusCode = 500;
                responseString = $"Error: {ex.Message}";
            }

            try
            {
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                context.Response.StatusCode = statusCode;
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();
            }
            catch
            {
                // Ignored
            }
        }

        private void Enqueue(Action action)
        {
            _mainThreadActions.Enqueue(action);
        }
    }
}

using System.IO;
using System.Net;
using System.Text;
using System;
using UnityEditor;
using UnityEngine;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Collections;
public class TinyWebServerWindow : EditorWindow
{
    private static string basePath;
    private static int port = 80;
    private static CancellationTokenSource cts;
    private static HttpListener server;
    private int processid;
    private static readonly string key_lastbuild = "webglLastbuild";
    private string key_marksvrrun;
    private static EditorWindow window;
    private volatile bool isWebServerTitleChangeRequired = true;
    private readonly static List<MIME> mimeTypes = new()
    {
            new(".wasm", "application/wasm"),
            new(".wasm.br", "application/wasm"),
            new(".wasm.gz", "application/wasm"),
            new(".js", "application/javascript"),
            new(".js.gz", "application/javascript"),
            new(".js.br", "application/javascript"),
            new(".data", "application/octet-stream"),
            new(".data.br", "application/octet-stream"),
            new(".data.gz", "application/gzip"),
            new(".gz", "application/gzip"),
            // Add more MIME types as needed
        };

    [MenuItem("Tools/Tiny WebServer")]
    private static void Init()
    {
        window = GetWindow<TinyWebServerWindow>();
        window.minSize = new Vector2(300, 180);
        var icon = EditorGUIUtility.IconContent("d_BuildSettings.Web.Small");
        window.titleContent = new GUIContent("Tiny WebServer", icon.image);
        window.Show();
    }

    private void TryRepaintTitleContent()
    {
        if (!isWebServerTitleChangeRequired)
        {
            return;
        }
        isWebServerTitleChangeRequired = false;
        window ??= GetWindow<TinyWebServerWindow>();

        var assembly = typeof(AssetDatabase).Assembly;
        var styleType = assembly.GetType("UnityEditor.DockArea+Styles");
        var style = styleType.GetField("tabLabel", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).GetValue(null) as GUIStyle;
        window.titleContent.text = "Tiny WebServer<*****=###>●</*****>";
        var calcSize = style.CalcSize(window.titleContent);
        styleType.GetField("tabMaxWidth", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).SetValue(null, calcSize.x);
        var contents = assembly.GetType("UnityEditor.DockArea").GetField("s_GUIContents", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
        (contents as IDictionary).Clear();
        window.titleContent.text = $"Tiny WebServer <color={(server?.IsListening == true ? "green" : "red")}>●</color>";
        window.titleContent.tooltip = server?.IsListening == true ? "Server is running" : "Server is stoped";
    }

    private void Awake()
    {
        processid = System.Diagnostics.Process.GetCurrentProcess().Id;
        key_marksvrrun = $"key_svrisruning{processid}";
    }
    private void OnEnable()
    {
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
    }

    private void OnDisable()
    {
        AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
        AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
    }

    private void OnDestroy()
    {
        EditorPrefs.DeleteKey(key_marksvrrun); // 只有销毁和用户停止Server才会删除这个key
        StopServer();
    }

    private void OnGUI()
    {
        TryRepaintTitleContent();
        basePath = EditorPrefs.GetString(key_lastbuild, Application.dataPath + "/../Builds/WebGL");
        port = EditorPrefs.GetInt("webglPort", 80);
        GUILayout.Label("Port: ");
        using (new EditorGUILayout.HorizontalScope())
        {
            port = EditorGUILayout.IntField(port);
            if (GUILayout.Button("Random", GUILayout.Width(100)))
            {
                port = GetAvailablePort();
                Debug.Log($"{nameof(TinyWebServerWindow)}: 端口已修改，下次开启 WebServer 时生效！ ");
            }
        }

        GUILayout.Label("Base Path: ");
        using (new GUILayout.HorizontalScope())
        {
            float textFieldWidth = EditorGUIUtility.currentViewWidth - 110;
            basePath = GUILayout.TextField(basePath, GUILayout.Width(textFieldWidth));
            if (GUILayout.Button("Select", GUILayout.Width(100)))
            {
                string path = EditorUtility.OpenFolderPanel("Select Base Path", basePath, "");
                if (path.Length != 0)
                {
                    basePath = path;
                    EditorPrefs.SetString(key_lastbuild, basePath);
                }
            }
        }

        if (GUI.changed)
        {
            EditorPrefs.SetInt("webglPort", port);
            EditorPrefs.SetString(key_lastbuild, basePath);
        }

        string buttonLabel = server?.IsListening == true ? "Stop Server" : "Run Server";
        if (GUILayout.Button(buttonLabel))
        {
            if (server?.IsListening == true)
            {
                EditorPrefs.DeleteKey(key_marksvrrun);
                StopServer();
            }
            else
            {
                Task.Run(RunServer);
            }
        }

        if (GUILayout.Button("Open Browser"))
        {
            System.Diagnostics.Process.Start("http://localhost:" + port + "/");
        }
    }

    private void StopServer()
    {
        if (server?.IsListening == true)
        {
            cts?.Cancel();
            server?.Stop();
            server = null;
            cts = null;
            isWebServerTitleChangeRequired = true;
            Debug.Log($"Server has Stoped!");
        }
    }

    private void OnBeforeAssemblyReload()
    {
        if (server?.IsListening == true)
        {
            EditorPrefs.SetBool(key_marksvrrun, true);
            StopServer();
        }
    }

    private void OnAfterAssemblyReload()
    {
        if (EditorPrefs.HasKey(key_marksvrrun))
        {
            Task.Run(RunServer);
            Debug.Log($"{nameof(TinyWebServerWindow)}:Restart as it used to be~");
        }
    }

    private void RunServer()
    {
        server = new HttpListener();
        cts = new CancellationTokenSource();
        server.Prefixes.Add("http://localhost:" + port + "/");
        Debug.Log("Web server start http://localhost:" + port + "/");
        server.Start();
        isWebServerTitleChangeRequired = true;
        while (!cts.IsCancellationRequested)
        {
            try
            {
                cts.Token.ThrowIfCancellationRequested();
                HandleRequest();
            }
            catch (Exception ex)
            {
                Debug.Log("Caught Exception handling request: " + ex);
            }
        }
    }

    private void HandleRequest()
    {
        HttpListenerContext context = server.GetContext();
        HttpListenerResponse response = context.Response;
        SetResponseHeaders(response);
        string path = Uri.UnescapeDataString(context.Request.Url.LocalPath);
        Debug.Log("Handling request: " + path);
        if (path == "/")
        {
            path = "/index.html";
        }
        SetCORSHeaders(path, response);
        SetContentEncoding(path, response);
        SetAcceptRanges(context, response);
        SetContentType(path, response);
        string page = basePath + path;
        HandleFile(page, context, response);
        response.Close();
    }

    private void SetResponseHeaders(HttpListenerResponse response)
    {
        response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        response.Headers["Pragma"] = "no-cache";
        response.Headers["Expires"] = "0";
    }

    private void SetCORSHeaders(string path, HttpListenerResponse response)
    {
        if (Path.GetExtension(path) == ".html" || path.EndsWith(".js") || path.EndsWith(".js.gz") || path.EndsWith(".js.br"))
        {
            response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
            response.Headers["Cross-Origin-Embedder-Policy"] = "require-corp";
            response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        }
    }

    private void SetContentEncoding(string path, HttpListenerResponse response)
    {
        if (Path.GetExtension(path) == ".gz")
        {
            response.AddHeader("Content-Encoding", "gzip");
        }
        else if (Path.GetExtension(path) == ".br")
        {
            response.AddHeader("Content-Encoding", "br");
        }
    }

    private void SetAcceptRanges(HttpListenerContext context, HttpListenerResponse response)
    {
        if (context.Request.Headers.Get("Range") != null)
        {
            response.AddHeader("Accept-Ranges", "bytes");
        }
    }

    private void SetContentType(string path, HttpListenerResponse response)
    {
        MIME mimeType = mimeTypes.Find(m => path.EndsWith(m.extension));
        if (mimeType != null)
        {
            response.ContentType = mimeType.type;
        }
    }

    private void HandleFile(string page, HttpListenerContext context, HttpListenerResponse response)
    {
        string msg = null;
        if (!context.Request.IsLocal)
        {
            Debug.Log("Forbidden.");
            msg = "<HTML><BODY>403 Forbidden.</BODY></HTML>";
            response.StatusCode = 403;
        }
        else if (!File.Exists(page))
        {
            Debug.Log("Not found.");
            msg = "<HTML><BODY>404 Not found.</BODY></HTML>";
            response.StatusCode = 404;
        }
        else
        {
            SendFile(page, context, response);
        }
        if (msg != null)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(msg);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
        }
    }

    private void SendFile(string page, HttpListenerContext context, HttpListenerResponse response)
    {
        FileStream fileStream = File.Open(page, FileMode.Open);
        BinaryReader reader = new BinaryReader(fileStream);
        try
        {
            response.ContentLength64 = fileStream.Length;
            byte[] buffer = reader.ReadBytes(4096);
            while (buffer.Length != 0)
            {
                cts.Token.ThrowIfCancellationRequested();
                response.OutputStream.Write(buffer, 0, buffer.Length);
                buffer = reader.ReadBytes(4096);
            }
        }
        catch (Exception ex)
        {
            Debug.Log("Caught Exception sending file: " + ex);
        }
        finally
        {
            reader.Close();
        }
    }

    /// <summary>
    /// Draw buttons on the header area of the window.
    /// Automatically called by unity.
    /// </summary>
    /// <param name="position"></param>
    private void ShowButton(Rect position)
    {
        if (GUI.Button(position, EditorGUIUtility.IconContent("_Help"), GUI.skin.FindStyle("IconButton")))
        {
            Application.OpenURL("https://www.jianshu.com/u/275cca6e5f17");
        }
    }

    //实现获取一个可用的 http 端口
    private static int GetAvailablePort()
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

[Serializable]
public class MIME
{
    public string extension;
    public string type;

    public MIME(string extension, string type)
    {
        this.extension = extension;
        this.type = type;
    }
}
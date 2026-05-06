#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

internal sealed class VLiveKitErrorLogExporter : EditorWindow
{
    private const string MenuRoot = "toshi/VLiveKit/";
    private const string OutputFolder = "Logs/VLiveKitConsoleLogs";

    private Vector2 scrollPosition;
    private bool errorsOnly = true;
    private bool includeStackTrace = true;
    private string previewText = "";
    private string lastOutputPath = "";

    [MenuItem(MenuRoot + "Error Log Exporter")]
    private static void OpenWindow()
    {
        var window = GetWindow<VLiveKitErrorLogExporter>("VLiveKit Logs");
        window.minSize = new Vector2(680f, 420f);
        window.RefreshPreview();
        window.Show();
    }

    [MenuItem(MenuRoot + "Export Console Errors")]
    private static void ExportConsoleErrors()
    {
        var path = Export(errorsOnly: true, includeStackTrace: true);
        if (!string.IsNullOrEmpty(path))
        {
            Debug.Log("VLiveKit console errors exported: " + path);
        }
    }

    [MenuItem(MenuRoot + "Copy Console Errors")]
    private static void CopyConsoleErrors()
    {
        var text = BuildLogText(errorsOnly: true, includeStackTrace: true);
        EditorGUIUtility.systemCopyBuffer = text;
        Debug.Log("VLiveKit console errors copied to clipboard.");
    }

    private void OnGUI()
    {
        DrawHeader();
        DrawOptions();
        DrawActions();
        DrawPreview();
    }

    private void DrawHeader()
    {
        EditorGUILayout.Space(8f);
        GUILayout.Label("VLiveKit Error Log Exporter", new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 });
        GUILayout.Label("Export or copy Unity Console output without selecting entries one by one.", EditorStyles.miniLabel);
        EditorGUILayout.Space(6f);
    }

    private void DrawOptions()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        errorsOnly = EditorGUILayout.ToggleLeft("Errors and exceptions only", errorsOnly);
        includeStackTrace = EditorGUILayout.ToggleLeft("Include stack traces", includeStackTrace);
        EditorGUILayout.EndVertical();
    }

    private void DrawActions()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Refresh Preview", GUILayout.Height(28f)))
        {
            RefreshPreview();
        }

        if (GUILayout.Button("Copy", GUILayout.Height(28f)))
        {
            previewText = BuildLogText(errorsOnly, includeStackTrace);
            EditorGUIUtility.systemCopyBuffer = previewText;
            ShowNotification(new GUIContent("Copied console log"));
        }

        if (GUILayout.Button("Export", GUILayout.Height(28f)))
        {
            lastOutputPath = Export(errorsOnly, includeStackTrace);
            if (!string.IsNullOrEmpty(lastOutputPath))
            {
                ShowNotification(new GUIContent("Exported console log"));
            }
        }
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(lastOutputPath))
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.SelectableLabel(lastOutputPath, EditorStyles.miniLabel, GUILayout.Height(18f));
            if (GUILayout.Button("Reveal", EditorStyles.miniButton, GUILayout.Width(70f)))
            {
                EditorUtility.RevealInFinder(lastOutputPath);
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawPreview()
    {
        if (string.IsNullOrEmpty(previewText))
        {
            RefreshPreview();
        }

        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        EditorGUILayout.TextArea(previewText, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    private void RefreshPreview()
    {
        previewText = BuildLogText(errorsOnly, includeStackTrace);
    }

    private static string Export(bool errorsOnly, bool includeStackTrace)
    {
        var text = BuildLogText(errorsOnly, includeStackTrace);
        var folder = Path.Combine(ProjectRoot, OutputFolder);
        Directory.CreateDirectory(folder);

        var fileName = (errorsOnly ? "VLiveKitConsoleErrors_" : "VLiveKitConsoleLogs_") + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
        var path = Path.Combine(folder, fileName);
        File.WriteAllText(path, text, Encoding.UTF8);
        AssetDatabase.Refresh();
        return path;
    }

    private static string BuildLogText(bool errorsOnly, bool includeStackTrace)
    {
        var entries = ConsoleLogReader.Read();
        var builder = new StringBuilder();
        builder.AppendLine("VLiveKit Console Log Export");
        builder.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        builder.AppendLine("Unity: " + Application.unityVersion);
        builder.AppendLine("Project: " + ProjectRoot);
        builder.AppendLine("Filter: " + (errorsOnly ? "Errors and exceptions only" : "All console entries"));
        builder.AppendLine(new string('-', 80));

        var count = 0;
        foreach (var entry in entries)
        {
            if (errorsOnly && !entry.IsError)
            {
                continue;
            }

            count++;
            builder.AppendLine("[" + entry.Kind + "] " + entry.Condition);
            if (!string.IsNullOrEmpty(entry.File))
            {
                builder.AppendLine("File: " + entry.File + (entry.Line > 0 ? ":" + entry.Line : ""));
            }

            if (includeStackTrace && !string.IsNullOrEmpty(entry.StackTrace))
            {
                builder.AppendLine(entry.StackTrace.TrimEnd());
            }

            builder.AppendLine(new string('-', 80));
        }

        if (count == 0)
        {
            builder.AppendLine(errorsOnly ? "No console errors were found." : "No console entries were found.");
        }

        return builder.ToString();
    }

    private static string ProjectRoot
    {
        get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); }
    }

    private readonly struct ConsoleLogEntry
    {
        public ConsoleLogEntry(string condition, string stackTrace, string file, int line, int mode)
        {
            Condition = condition ?? "";
            StackTrace = stackTrace ?? "";
            File = file ?? "";
            Line = line;
            Mode = mode;
        }

        public string Condition { get; }
        public string StackTrace { get; }
        public string File { get; }
        public int Line { get; }
        public int Mode { get; }
        public bool IsError { get { return (Mode & 1) != 0 || (Mode & 2) != 0 || (Mode & 16) != 0 || (Mode & 64) != 0 || Condition.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0; } }
        public string Kind
        {
            get
            {
                if ((Mode & 8) != 0)
                {
                    return "Warning";
                }

                return IsError ? "Error" : "Log";
            }
        }
    }

    private static class ConsoleLogReader
    {
        public static List<ConsoleLogEntry> Read()
        {
            var entries = new List<ConsoleLogEntry>();
            var logEntriesType = FindEditorType("UnityEditor.LogEntries");
            var logEntryType = FindEditorType("UnityEditor.LogEntry");
            if (logEntriesType == null || logEntryType == null)
            {
                entries.Add(new ConsoleLogEntry("Unity Console reflection API was not found.", "", "", 0, 1));
                return entries;
            }

            var getCountMethod = logEntriesType.GetMethod("GetCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var startMethod = logEntriesType.GetMethod("StartGettingEntries", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var endMethod = logEntriesType.GetMethod("EndGettingEntries", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var getEntryMethod = logEntriesType.GetMethod("GetEntryInternal", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (getCountMethod == null || getEntryMethod == null)
            {
                entries.Add(new ConsoleLogEntry("Unity Console entry reader method was not found.", "", "", 0, 1));
                return entries;
            }

            var count = (int)getCountMethod.Invoke(null, null);
            startMethod?.Invoke(null, null);
            try
            {
                for (var i = 0; i < count; i++)
                {
                    var entry = Activator.CreateInstance(logEntryType);
                    var ok = (bool)getEntryMethod.Invoke(null, new[] { (object)i, entry });
                    if (!ok)
                    {
                        continue;
                    }

                    entries.Add(new ConsoleLogEntry(
                        GetString(logEntryType, entry, "condition"),
                        GetString(logEntryType, entry, "stackTrace"),
                        GetString(logEntryType, entry, "file"),
                        GetInt(logEntryType, entry, "line"),
                        GetInt(logEntryType, entry, "mode")));
                }
            }
            finally
            {
                endMethod?.Invoke(null, null);
            }

            return entries;
        }

        private static string GetString(Type type, object instance, string name)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field == null ? "" : field.GetValue(instance) as string ?? "";
        }

        private static int GetInt(Type type, object instance, string name)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                return 0;
            }

            var value = field.GetValue(instance);
            return value is int intValue ? intValue : 0;
        }

        private static Type FindEditorType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(typeName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }
}
#endif

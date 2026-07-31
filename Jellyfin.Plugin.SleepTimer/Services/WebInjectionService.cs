using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SleepTimer.Services;

/// <summary>
/// Makes the embedded client script available in Jellyfin Web.
/// </summary>
public sealed partial class WebInjectionService : IHostedService
{
    private const string StartMarker = "<!-- BEGIN Sleep Timer Plugin -->";
    private const string EndMarker = "<!-- END Sleep Timer Plugin -->";
    private const string ScriptId = "22cf1cd3-eefa-4126-8368-6b79872d2632-client";
    private static readonly Guid TransformationId =
        Guid.Parse("25e106c1-71ab-46d8-a304-40a8ab678e3a");

    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<WebInjectionService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebInjectionService"/> class.
    /// </summary>
    /// <param name="applicationPaths">Jellyfin application paths.</param>
    /// <param name="logger">Service logger.</param>
    public WebInjectionService(
        IApplicationPaths applicationPaths,
        ILogger<WebInjectionService> logger)
    {
        _applicationPaths = applicationPaths;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (TryRegisterWithFileTransformation())
        {
            return Task.CompletedTask;
        }

        if (TryRegisterWithJavaScriptInjector())
        {
            return Task.CompletedTask;
        }

        _logger.LogWarning(
            "Neither File Transformation nor JavaScript Injector is available; using the index.html fallback");
        InjectIntoIndex(_applicationPaths.WebPath, _logger);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds or refreshes the marked script block.
    /// </summary>
    /// <param name="html">Jellyfin Web index HTML.</param>
    /// <returns>Updated HTML.</returns>
    public static string ApplyInjection(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return html;
        }

        var withoutExistingBlock = InjectionBlockRegex().Replace(html, string.Empty);
        var bodyClosingIndex = withoutExistingBlock.LastIndexOf(
            "</body>",
            StringComparison.OrdinalIgnoreCase);

        if (bodyClosingIndex < 0)
        {
            return html;
        }

        var block = BuildInjectionBlock();
        return withoutExistingBlock.Insert(bodyClosingIndex, $"{block}{Environment.NewLine}");
    }

    /// <summary>
    /// Removes fallback injection during uninstall.
    /// </summary>
    /// <param name="webPath">Jellyfin Web directory.</param>
    /// <param name="logger">Logger.</param>
    public static void RemoveFallbackInjection(string webPath, ILogger logger)
    {
        var indexPath = Path.Combine(webPath, "index.html");
        if (!File.Exists(indexPath))
        {
            return;
        }

        try
        {
            var html = File.ReadAllText(indexPath);
            var updated = InjectionBlockRegex().Replace(html, string.Empty);
            if (!string.Equals(html, updated, StringComparison.Ordinal))
            {
                File.WriteAllText(indexPath, updated);
                logger.LogInformation("Removed the Sleep Timer block from {IndexPath}", indexPath);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not remove the Sleep Timer block from {IndexPath}",
                indexPath);
        }
    }

    /// <summary>
    /// Removes a script registration from JavaScript Injector during uninstall.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public static void UnregisterFromJavaScriptInjector(ILogger logger)
    {
        try
        {
            var assembly = FindLoadedAssembly("Jellyfin.Plugin.JavaScriptInjector");
            var interfaceType = assembly?.GetType(
                "Jellyfin.Plugin.JavaScriptInjector.PluginInterface");
            var method = interfaceType?.GetMethod(
                "UnregisterScript",
                BindingFlags.Public | BindingFlags.Static);

            method?.Invoke(null, [ScriptId]);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not unregister Sleep Timer from JavaScript Injector");
        }
    }

    private static string BuildInjectionBlock()
    {
        var version = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "1.0.0.0";
        return $"""
            {StartMarker}
            <script defer src="../SleepTimer/client.js?v={version}"></script>
            {EndMarker}
            """;
    }

    private static void InjectIntoIndex(string webPath, ILogger logger)
    {
        var indexPath = Path.Combine(webPath, "index.html");
        if (!File.Exists(indexPath))
        {
            logger.LogError(
                "Jellyfin Web index.html was not found at {IndexPath}. Install File Transformation or JavaScript Injector to enable the player button",
                indexPath);
            return;
        }

        try
        {
            var html = File.ReadAllText(indexPath);
            var updated = ApplyInjection(html);

            if (string.Equals(html, updated, StringComparison.Ordinal))
            {
                logger.LogInformation("Sleep Timer client block is already installed");
                return;
            }

            File.WriteAllText(indexPath, updated);
            logger.LogInformation(
                "Injected the Sleep Timer client block into {IndexPath}",
                indexPath);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not inject Sleep Timer into {IndexPath}. Install File Transformation to use non-destructive injection",
                indexPath);
        }
    }

    private bool TryRegisterWithFileTransformation()
    {
        try
        {
            var assembly = FindLoadedAssembly("Jellyfin.Plugin.FileTransformation");
            var interfaceType = assembly?.GetType(
                "Jellyfin.Plugin.FileTransformation.PluginInterface");
            var method = interfaceType?.GetMethod(
                "RegisterTransformation",
                BindingFlags.Public | BindingFlags.Static);

            if (method is null)
            {
                return false;
            }

            var payload = CreateJsonObjectForMethod(
                method,
                new Dictionary<string, object?>
                {
                    ["id"] = TransformationId,
                    ["fileNamePattern"] = "index.html",
                    ["callbackAssembly"] = GetType().Assembly.FullName,
                    ["callbackClass"] = typeof(IndexTransformation).FullName,
                    ["callbackMethod"] = nameof(IndexTransformation.Transform)
                });

            method.Invoke(null, [payload]);
            _logger.LogInformation(
                "Registered Sleep Timer client injection with File Transformation");
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "File Transformation registration failed; trying the next injection method");
            return false;
        }
    }

    private bool TryRegisterWithJavaScriptInjector()
    {
        try
        {
            var assembly = FindLoadedAssembly("Jellyfin.Plugin.JavaScriptInjector");
            var interfaceType = assembly?.GetType(
                "Jellyfin.Plugin.JavaScriptInjector.PluginInterface");
            var method = interfaceType?.GetMethod(
                "RegisterScript",
                BindingFlags.Public | BindingFlags.Static);

            if (method is null)
            {
                return false;
            }

            var resourceName = $"{typeof(Plugin).Namespace}.Web.client.js";
            using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                _logger.LogError(
                    "Embedded client script {ResourceName} was not found",
                    resourceName);
                return false;
            }

            using var reader = new StreamReader(stream);
            var script = reader.ReadToEnd();
            var payload = CreateJsonObjectForMethod(
                method,
                new Dictionary<string, object?>
                {
                    ["id"] = ScriptId,
                    ["name"] = "Sleep Timer Client",
                    ["script"] = script,
                    ["enabled"] = true,
                    ["requiresAuthentication"] = true,
                    ["pluginId"] = Plugin.Instance?.Id.ToString(),
                    ["pluginName"] = Plugin.Instance?.Name ?? "Sleep Timer",
                    ["pluginVersion"] = typeof(Plugin).Assembly.GetName().Version?.ToString()
                });

            var result = method.Invoke(null, [payload]);
            if (result is bool registered && !registered)
            {
                return false;
            }

            _logger.LogInformation(
                "Registered Sleep Timer client with JavaScript Injector");
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "JavaScript Injector registration failed; using the index.html fallback");
            return false;
        }
    }

    private static object CreateJsonObjectForMethod(
        MethodInfo targetMethod,
        IDictionary<string, object?> values)
    {
        var parameterType = targetMethod.GetParameters().Single().ParameterType;
        var fromObject = parameterType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "FromObject", StringComparison.Ordinal) &&
                candidate.GetParameters().Length == 1 &&
                candidate.GetParameters()[0].ParameterType == typeof(object));

        return fromObject?.Invoke(null, [values])
            ?? throw new InvalidOperationException(
                $"Could not create a {parameterType.FullName} payload.");
    }

    private static Assembly? FindLoadedAssembly(string assemblyNameFragment)
    {
        return AssemblyLoadContext.All
            .SelectMany(context => context.Assemblies)
            .FirstOrDefault(assembly =>
                assembly.FullName?.Contains(
                    assemblyNameFragment,
                    StringComparison.OrdinalIgnoreCase) == true);
    }

    [GeneratedRegex(
        "<!-- BEGIN Sleep Timer Plugin -->[\\s\\S]*?<!-- END Sleep Timer Plugin -->\\s*",
        RegexOptions.CultureInvariant)]
    private static partial Regex InjectionBlockRegex();
}

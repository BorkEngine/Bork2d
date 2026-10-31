using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Bork2d.Engine;
using Bork2d.Render.Diagnostics;
using Microsoft.Extensions.Logging;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The production Vulkan instance and validation/debug-utils bootstrap.
/// </summary>
/// <remarks>
/// <para>
/// Windowed instances are created on the OS/window thread with SDL's extension list; offscreen
/// instances are created on the render thread with no surface extensions. The class never calls
/// SDL and never creates a device, surface, or swapchain.
/// </para>
/// <para>
/// The instance requests Vulkan 1.3. A loader that cannot report 1.3 is a startup failure naming
/// the reported version. When validation is requested and the layer is absent, a warning is
/// logged and the instance continues without validation (D-M6-20). The debug messenger is
/// installed only when validation is enabled and <c>VK_EXT_debug_utils</c> is available; messages
/// are routed to the <c>Bork2d.Render</c> category, and errors are fatal only when validation is
/// enabled (D-M6-21).
/// </para>
/// </remarks>
internal sealed unsafe class VulkanInstance : IVulkanInstance
{
    private static readonly DebugUtilsMessengerCallbackFunctionEXT _debugCallback =
        HandleDebugUtilsMessage;

    private readonly Vk _vk;
    private readonly Instance _instance;
    private readonly ILogger _logger;
    private readonly string[] _enabledLayers;
    private readonly string[] _enabledExtensions;
    private readonly bool _validationEnabled;
    private readonly bool _debugUtilsRequested;
    private ExtDebugUtils? _debugUtils;
    private DebugUtilsMessengerEXT _debugMessenger;
    private GCHandle _selfHandle;
    private int _errorCount;
    private int _warningCount;
    private Exception? _fatalException;
    private int _destroyed;

    private VulkanInstance(
        Vk vk,
        Instance instance,
        ILogger logger,
        string[] enabledLayers,
        string[] enabledExtensions,
        bool validationEnabled,
        bool debugUtilsRequested)
    {
        _vk = vk;
        _instance = instance;
        _logger = logger;
        _enabledLayers = enabledLayers;
        _enabledExtensions = enabledExtensions;
        _validationEnabled = validationEnabled;
        _debugUtilsRequested = debugUtilsRequested;
    }

    /// <inheritdoc/>
    public nint Handle => _instance.Handle;

    /// <inheritdoc/>
    public bool ValidationEnabled => _validationEnabled;

    /// <inheritdoc/>
    public bool DebugUtilsEnabled => _debugUtils is not null;

    /// <inheritdoc/>
    public IReadOnlyList<string> EnabledLayers => _enabledLayers;

    /// <inheritdoc/>
    public IReadOnlyList<string> EnabledExtensions => _enabledExtensions;

    /// <inheritdoc/>
    public int ValidationErrorCount => Volatile.Read(ref _errorCount);

    /// <inheritdoc/>
    public int ValidationWarningCount => Volatile.Read(ref _warningCount);

    /// <inheritdoc/>
    public Exception? FatalException => Volatile.Read(ref _fatalException);

    /// <summary>Creates a Vulkan instance for an explicit request.</summary>
    /// <param name="request">The validated creation request.</param>
    /// <returns>The created instance.</returns>
    /// <exception cref="Bork2dException">
    /// The loader is unavailable, the loader/instance cannot provide Vulkan 1.3, or a required
    /// instance extension is missing.
    /// </exception>
    internal static VulkanInstance Create(VulkanInstanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.LoggerFactory);

        ILogger logger = request.LoggerFactory.CreateLogger(RenderLog.Category);

        Vk vk;
        try
        {
            vk = Vk.GetApi();
        }
        catch (Exception exception)
        {
            throw new Bork2dException(
                "The Vulkan loader (vulkan-1.dll) could not be loaded. Windowed and offscreen "
                    + "Bork2d apps require a Vulkan 1.3 driver and loader.",
                exception);
        }

        try
        {
            return CreateCore(vk, request, logger);
        }
        catch
        {
            vk.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public void Destroy()
    {
        if (Interlocked.Exchange(ref _destroyed, 1) != 0)
        {
            return;
        }

        try
        {
            if (_debugUtils is not null && _debugMessenger.Handle != 0)
            {
                _debugUtils.DestroyDebugUtilsMessenger(_instance, _debugMessenger, null);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Vulkan debug messenger destruction failed.");
        }
        finally
        {
            _debugMessenger = default;
            _debugUtils = null;
            if (_selfHandle.IsAllocated)
            {
                _selfHandle.Free();
            }
        }

        try
        {
            _vk.DestroyInstance(_instance, null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Vulkan instance destruction failed.");
        }
        finally
        {
            _vk.CurrentInstance = null;
            _vk.Dispose();
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Destroy();

    /// <inheritdoc/>
    public void DestroySurface(nint surface)
    {
        if (surface == 0 || Volatile.Read(ref _destroyed) != 0)
        {
            return;
        }

        try
        {
            if (!_vk.TryGetInstanceExtension(_instance, out KhrSurface surfaceApi))
            {
                _logger.LogError(
                    "Vulkan surface destruction failed: {Extension} is not available.",
                    VulkanNames.SurfaceExtension);
                return;
            }

            surfaceApi.DestroySurface(_instance, new SurfaceKHR((ulong)surface), null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Vulkan surface destruction failed.");
        }
    }

    /// <inheritdoc/>
    public void SetObjectName(
        nint device,
        ObjectType objectType,
        nint objectHandle,
        string name)
    {
        if (_debugUtils is null || string.IsNullOrEmpty(name))
        {
            return;
        }

        nint namePointer = 0;
        try
        {
            namePointer = SilkMarshal.StringToPtr(name, NativeStringEncoding.UTF8);
            var info = new DebugUtilsObjectNameInfoEXT
            {
                SType = StructureType.DebugUtilsObjectNameInfoExt,
                ObjectType = objectType,
                ObjectHandle = (ulong)objectHandle,
                PObjectName = (byte*)namePointer,
            };

            Result result = _debugUtils.SetDebugUtilsObjectName(
                new Device((nint)device),
                &info);
            if (result != Result.Success)
            {
                _logger.LogDebug(
                    "Vulkan object naming returned {Result} for {ObjectType} {ObjectHandle}.",
                    result,
                    objectType,
                    objectHandle);
            }
        }
        finally
        {
            if (namePointer != 0)
            {
                SilkMarshal.FreeString(namePointer, NativeStringEncoding.UTF8);
            }
        }
    }

    /// <inheritdoc/>
    public void BeginCommandBufferLabel(nint device, nint commandBuffer, string label)
    {
        if (_debugUtils is null || string.IsNullOrEmpty(label))
        {
            return;
        }

        nint labelPointer = 0;
        try
        {
            labelPointer = SilkMarshal.StringToPtr(label, NativeStringEncoding.UTF8);
            var info = new DebugUtilsLabelEXT
            {
                SType = StructureType.DebugUtilsLabelExt,
                PLabelName = (byte*)labelPointer,
            };

            _debugUtils.CmdBeginDebugUtilsLabel(new CommandBuffer((nint)commandBuffer), &info);
        }
        finally
        {
            if (labelPointer != 0)
            {
                SilkMarshal.FreeString(labelPointer, NativeStringEncoding.UTF8);
            }
        }
    }

    /// <inheritdoc/>
    public void EndCommandBufferLabel(nint device, nint commandBuffer)
    {
        if (_debugUtils is null)
        {
            return;
        }

        _debugUtils.CmdEndDebugUtilsLabel(new CommandBuffer((nint)commandBuffer));
    }

    /// <inheritdoc/>
    public nint CreateDebugLabel(string label)
    {
        if (_debugUtils is null || string.IsNullOrEmpty(label))
        {
            return 0;
        }

        return SilkMarshal.StringToPtr(label, NativeStringEncoding.UTF8);
    }

    /// <inheritdoc/>
    public void DestroyDebugLabel(nint label)
    {
        if (label == 0)
        {
            return;
        }

        SilkMarshal.FreeString(label, NativeStringEncoding.UTF8);
    }

    /// <inheritdoc/>
    public void BeginCachedCommandBufferLabel(nint device, nint commandBuffer, nint label)
    {
        if (_debugUtils is null || label == 0)
        {
            return;
        }

        var info = new DebugUtilsLabelEXT
        {
            SType = StructureType.DebugUtilsLabelExt,
            PLabelName = (byte*)label,
        };

        _debugUtils.CmdBeginDebugUtilsLabel(new CommandBuffer((nint)commandBuffer), &info);
    }

    private static VulkanInstance CreateCore(
        Vk vk,
        VulkanInstanceRequest request,
        ILogger logger)
    {
        uint loaderVersion = 0;
        Result versionResult = vk.EnumerateInstanceVersion(ref loaderVersion);
        if (versionResult != Result.Success)
        {
            throw new Bork2dException(
                "The Vulkan loader could not report its instance version "
                    + $"(VkResult {versionResult}).");
        }

        if (loaderVersion < VulkanVersion.ApiVersion13)
        {
            throw new Bork2dException(
                $"The Vulkan loader reports API version {VulkanVersion.Format(loaderVersion)}; "
                    + "Bork2d requires Vulkan 1.3.0.");
        }

        logger.LogDebug(
            "Vulkan loader reports API version {LoaderVersion}.",
            VulkanVersion.Format(loaderVersion));

        string[] availableLayers = EnumerateInstanceLayers(vk);
        var validationLayerAvailable = Contains(availableLayers, VulkanNames.ValidationLayer);
        var validationEnabled = request.ValidationRequested && validationLayerAvailable;
        if (request.ValidationRequested && !validationLayerAvailable)
        {
            logger.LogWarning(
                "Vulkan validation was requested, but the layer {Layer} is not available; "
                    + "the run continues without validation.",
                VulkanNames.ValidationLayer);
        }

        string[] availableExtensions = EnumerateInstanceExtensions(vk);
        string[] requiredExtensions = NormalizeExtensions(request.RequiredExtensions);
        List<string> missingExtensions = requiredExtensions
            .Where(extension => !Contains(availableExtensions, extension))
            .ToList();
        if (missingExtensions.Count > 0)
        {
            throw new Bork2dException(
                "The Vulkan instance is missing required extension(s): "
                    + $"{string.Join(", ", missingExtensions)}.");
        }

        var debugUtilsAvailable = Contains(availableExtensions, VulkanNames.DebugUtilsExtension);
        if (validationEnabled && !debugUtilsAvailable)
        {
            logger.LogWarning(
                "Vulkan validation is enabled, but {Extension} is not available; "
                    + "validation messages and debug labels will not be captured.",
                VulkanNames.DebugUtilsExtension);
        }

        var enabledExtensions = new List<string>(requiredExtensions);
        var debugUtilsRequested = validationEnabled && debugUtilsAvailable;
        if (debugUtilsRequested && !enabledExtensions.Contains(VulkanNames.DebugUtilsExtension))
        {
            enabledExtensions.Add(VulkanNames.DebugUtilsExtension);
        }

        string[] enabledLayers = validationEnabled ? [VulkanNames.ValidationLayer] : [];
        string[] finalExtensions = [.. enabledExtensions];

        Instance instance = CreateInstanceHandle(
            vk,
            request,
            enabledLayers,
            finalExtensions);

        vk.CurrentInstance = instance;

        var created = new VulkanInstance(
            vk,
            instance,
            logger,
            enabledLayers,
            finalExtensions,
            validationEnabled,
            debugUtilsRequested);

        try
        {
            created.EnableDebugUtils();
        }
        catch
        {
            created.Destroy();
            throw;
        }

        logger.LogInformation(
            "Vulkan instance created: apiVersion={ApiVersion}, validation={Validation}, "
                + "layers=[{Layers}], extensions=[{Extensions}].",
            VulkanVersion.Format(VulkanVersion.ApiVersion13),
            validationEnabled ? "on" : "off",
            string.Join(", ", enabledLayers),
            string.Join(", ", finalExtensions));

        return created;
    }

    private static Instance CreateInstanceHandle(
        Vk vk,
        VulkanInstanceRequest request,
        IReadOnlyList<string> layers,
        IReadOnlyList<string> extensions)
    {
        nint applicationNamePointer = 0;
        nint engineNamePointer = 0;
        nint layerPointer = 0;
        nint extensionPointer = 0;

        try
        {
            string applicationName = string.IsNullOrWhiteSpace(request.ApplicationName)
                ? DefaultApplicationName()
                : request.ApplicationName;
            string engineName = string.IsNullOrWhiteSpace(request.EngineName)
                ? "Bork2d"
                : request.EngineName;

            applicationNamePointer = SilkMarshal.StringToPtr(
                applicationName,
                NativeStringEncoding.UTF8);
            engineNamePointer = SilkMarshal.StringToPtr(engineName, NativeStringEncoding.UTF8);
            if (layers.Count > 0)
            {
                layerPointer = SilkMarshal.StringArrayToPtr(layers, NativeStringEncoding.UTF8);
            }

            if (extensions.Count > 0)
            {
                extensionPointer = SilkMarshal.StringArrayToPtr(
                    extensions,
                    NativeStringEncoding.UTF8);
            }

            var applicationInfo = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = (byte*)applicationNamePointer,
                ApplicationVersion = 0,
                PEngineName = (byte*)engineNamePointer,
                EngineVersion = 0,
                ApiVersion = VulkanVersion.ApiVersion13,
            };

            var createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &applicationInfo,
                EnabledLayerCount = (uint)layers.Count,
                PpEnabledLayerNames = layerPointer == 0 ? null : (byte**)layerPointer,
                EnabledExtensionCount = (uint)extensions.Count,
                PpEnabledExtensionNames = extensionPointer == 0 ? null : (byte**)extensionPointer,
            };

            Result result = vk.CreateInstance(&createInfo, null, out Instance instance);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkCreateInstance failed with VkResult {result}. The machine may not provide "
                        + "a Vulkan 1.3 loader/instance.");
            }

            return instance;
        }
        finally
        {
            FreeString(applicationNamePointer);
            FreeString(engineNamePointer);
            FreeString(layerPointer);
            FreeString(extensionPointer);
        }
    }

    private void EnableDebugUtils()
    {
        if (!_debugUtilsRequested)
        {
            return;
        }

        if (!_vk.TryGetInstanceExtension(_instance, out ExtDebugUtils debugUtils))
        {
            _logger.LogWarning(
                "Vulkan debug utils could not be loaded; validation messages and debug labels "
                    + "will not be captured.");
            return;
        }

        _selfHandle = GCHandle.Alloc(this, GCHandleType.Normal);
        var createInfo = new DebugUtilsMessengerCreateInfoEXT
        {
            SType = StructureType.DebugUtilsMessengerCreateInfoExt,
            MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt
                | DebugUtilsMessageSeverityFlagsEXT.InfoBitExt
                | DebugUtilsMessageSeverityFlagsEXT.WarningBitExt
                | DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt,
            MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt
                | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt
                | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt,
            PfnUserCallback = _debugCallback,
            PUserData = (void*)GCHandle.ToIntPtr(_selfHandle),
        };

        Result result = debugUtils.CreateDebugUtilsMessenger(
            _instance,
            &createInfo,
            null,
            out _debugMessenger);
        if (result != Result.Success)
        {
            _logger.LogWarning(
                "Vulkan debug messenger creation returned {Result}; validation messages will not "
                    + "be captured.",
                result);
            _debugMessenger = default;
            _selfHandle.Free();
            return;
        }

        _debugUtils = debugUtils;
    }

    private static string[] EnumerateInstanceLayers(Vk vk)
    {
        uint count = 0;
        Result result = vk.EnumerateInstanceLayerProperties(ref count, null);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkEnumerateInstanceLayerProperties failed with VkResult {result}.");
        }

        if (count == 0)
        {
            return [];
        }

        var layers = new LayerProperties[count];
        fixed (LayerProperties* layersPointer = layers)
        {
            result = vk.EnumerateInstanceLayerProperties(ref count, layersPointer);
        }

        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkEnumerateInstanceLayerProperties failed with VkResult {result}.");
        }

        var names = new string[count];
        for (var index = 0; index < count; index++)
        {
            fixed (byte* namePointer = layers[index].LayerName)
            {
                names[index] = ReadNullTerminated(namePointer);
            }
        }

        return names;
    }

    private static string[] EnumerateInstanceExtensions(Vk vk)
    {
        uint count = 0;
        Result result = vk.EnumerateInstanceExtensionProperties(
            (byte*)null,
            ref count,
            null);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkEnumerateInstanceExtensionProperties failed with VkResult {result}.");
        }

        if (count == 0)
        {
            return [];
        }

        var extensions = new ExtensionProperties[count];
        fixed (ExtensionProperties* extensionsPointer = extensions)
        {
            result = vk.EnumerateInstanceExtensionProperties(
                (byte*)null,
                ref count,
                extensionsPointer);
        }

        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkEnumerateInstanceExtensionProperties failed with VkResult {result}.");
        }

        var names = new string[count];
        for (var index = 0; index < count; index++)
        {
            fixed (byte* namePointer = extensions[index].ExtensionName)
            {
                names[index] = ReadNullTerminated(namePointer);
            }
        }

        return names;
    }

    private static string ReadNullTerminated(byte* value)
        => Encoding.UTF8.GetString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(value));

    private static string[] NormalizeExtensions(IReadOnlyList<string>? extensions)
    {
        if (extensions is null || extensions.Count == 0)
        {
            return [];
        }

        var result = new List<string>(extensions.Count);
        foreach (string? extension in extensions)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                throw new ArgumentException(
                    "A Vulkan instance extension name cannot be null or empty.",
                    nameof(extensions));
            }

            if (!result.Contains(extension))
            {
                result.Add(extension);
            }
        }

        return [.. result];
    }

    private static bool Contains(IReadOnlyList<string> names, string name)
    {
        for (var index = 0; index < names.Count; index++)
        {
            if (string.Equals(names[index], name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string DefaultApplicationName()
        => Assembly.GetEntryAssembly()?.GetName().Name ?? "Bork2d";

    private static void FreeString(nint pointer)
    {
        if (pointer != 0)
        {
            SilkMarshal.FreeString(pointer, NativeStringEncoding.UTF8);
        }
    }

    private static uint HandleDebugUtilsMessage(
        DebugUtilsMessageSeverityFlagsEXT severity,
        DebugUtilsMessageTypeFlagsEXT type,
        DebugUtilsMessengerCallbackDataEXT* callbackData,
        void* userData)
    {
        try
        {
            if (callbackData is null || userData is null)
            {
                return 0;
            }

            if (GCHandle.FromIntPtr((nint)userData).Target is not VulkanInstance instance)
            {
                return 0;
            }

            instance.HandleDebugMessage(severity, type, callbackData);
        }
        catch
        {
            // A validation callback must never throw across the native boundary.
        }

        return 0;
    }

    private void HandleDebugMessage(
        DebugUtilsMessageSeverityFlagsEXT severity,
        DebugUtilsMessageTypeFlagsEXT type,
        DebugUtilsMessengerCallbackDataEXT* callbackData)
    {
        string message = callbackData->PMessage is null
            ? string.Empty
            : Marshal.PtrToStringUTF8((nint)callbackData->PMessage) ?? string.Empty;
        string messageIdName = callbackData->PMessageIdName is null
            ? string.Empty
            : Marshal.PtrToStringUTF8((nint)callbackData->PMessageIdName) ?? string.Empty;
        int messageId = callbackData->MessageIdNumber;
        string objects = DescribeObjects(callbackData);

        if ((severity & DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt) != 0)
        {
            Interlocked.Increment(ref _errorCount);
            var fatal = new Bork2dException(
                $"Vulkan validation error {messageIdName} ({messageId}): {message}");
            Interlocked.CompareExchange(ref _fatalException, fatal, null);
            _logger.LogError(
                "Vulkan validation error: severity={Severity}, id={MessageId}, "
                    + "name={MessageIdName}, objects={Objects}, message={Message}.",
                severity,
                messageId,
                messageIdName,
                objects,
                message);
            return;
        }

        if ((severity & DebugUtilsMessageSeverityFlagsEXT.WarningBitExt) != 0)
        {
            Interlocked.Increment(ref _warningCount);
            _logger.LogWarning(
                "Vulkan validation warning: severity={Severity}, id={MessageId}, "
                    + "name={MessageIdName}, objects={Objects}, message={Message}.",
                severity,
                messageId,
                messageIdName,
                objects,
                message);
            return;
        }

        _logger.LogDebug(
            "Vulkan validation message: severity={Severity}, type={Type}, id={MessageId}, "
                + "name={MessageIdName}, objects={Objects}, message={Message}.",
            severity,
            type,
            messageId,
            messageIdName,
            objects,
            message);
    }

    private static string DescribeObjects(DebugUtilsMessengerCallbackDataEXT* callbackData)
    {
        if (callbackData->ObjectCount == 0 || callbackData->PObjects is null)
        {
            return "(none)";
        }

        var builder = new StringBuilder();
        for (uint index = 0; index < callbackData->ObjectCount; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            DebugUtilsObjectNameInfoEXT item = callbackData->PObjects[index];
            builder.Append(item.ObjectType)
                .Append("(0x")
                .Append(item.ObjectHandle.ToString("X"))
                .Append(')');
            if (item.PObjectName is not null)
            {
                string? name = Marshal.PtrToStringUTF8((nint)item.PObjectName);
                if (!string.IsNullOrEmpty(name))
                {
                    builder.Append(' ').Append(name);
                }
            }
        }

        return builder.ToString();
    }
}

using System.Runtime.InteropServices;
using System.Text;
using Bork2d.Engine;
using Bork2d.Render.Diagnostics;
using Microsoft.Extensions.Logging;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The production physical-device selection and logical-device creation policy application.
/// </summary>
/// <remarks>
/// Enumeration and acceptance are logged once at Debug; the selected device is logged once at
/// Information. The class creates no surface or swapchain, and it never retries a rejected device
/// or asks the user to choose.
/// </remarks>
internal static unsafe class VulkanDeviceSelector
{
    /// <summary>
    /// Enumerates every physical device, applies <see cref="VulkanDevicePolicy"/>, and creates the
    /// logical device for the preferred accepted candidate.
    /// </summary>
    /// <param name="instance">The instance created by the platform or the offscreen renderer.</param>
    /// <param name="requirePresent">Whether the graphics family must also present (windowed).</param>
    /// <param name="surfaceHandle">The opaque surface handle (windowed), or <c>0</c>.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> category logger.</param>
    /// <returns>The selected device. The caller owns and destroys it.</returns>
    /// <exception cref="Bork2dException">No suitable device or device creation failed.</exception>
    internal static VulkanDevice Select(
        IVulkanInstance instance,
        bool requirePresent,
        nint surfaceHandle,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(logger);

        if (requirePresent && surfaceHandle == 0)
        {
            throw new ArgumentException(
                "Windowed device selection requires a surface handle.",
                nameof(surfaceHandle));
        }

        Vk vk;
        try
        {
            vk = Vk.GetApi();
        }
        catch (Exception exception)
        {
            throw new Bork2dException(
                "The Vulkan loader (vulkan-1.dll) could not be loaded while selecting a device.",
                exception);
        }

        try
        {
            var instanceHandle = new Instance(instance.Handle);
            vk.CurrentInstance = instanceHandle;

            List<PhysicalDevice> physicalDevices = [.. vk.GetPhysicalDevices(instanceHandle)];
            if (physicalDevices.Count == 0)
            {
                throw new Bork2dException(
                    "No Vulkan physical devices were found. Bork2d windowed and offscreen modes "
                        + "require a Vulkan 1.3 device.");
            }

            KhrSurface? surfaceApi = null;
            if (requirePresent)
            {
                if (!vk.TryGetInstanceExtension(instanceHandle, out KhrSurface loadedSurfaceApi))
                {
                    throw new Bork2dException(
                        $"Windowed device selection requires {VulkanNames.SurfaceExtension}, but it "
                            + "is not available on the instance.");
                }

                surfaceApi = loadedSurfaceApi;
            }

            var evaluations = new List<DeviceEvaluation>(physicalDevices.Count);
            for (var index = 0; index < physicalDevices.Count; index++)
            {
                DeviceCandidate candidate = DescribeCandidate(
                    vk,
                    physicalDevices[index],
                    surfaceApi,
                    surfaceHandle);
                DeviceEvaluation evaluation = VulkanDevicePolicy.Evaluate(candidate, requirePresent);
                evaluations.Add(evaluation);
                LogCandidate(logger, evaluation);
            }

            int selected = VulkanDevicePolicy.SelectPreferred(evaluations);
            if (selected < 0)
            {
                throw new Bork2dException(DescribeNoSuitableDevice(evaluations, requirePresent));
            }

            DeviceEvaluation selectedEvaluation = evaluations[selected];
            return CreateDevice(
                vk,
                instance,
                instanceHandle,
                physicalDevices[selected],
                selectedEvaluation,
                requirePresent,
                logger);
        }
        catch
        {
            vk.Dispose();
            throw;
        }
    }

    private static DeviceCandidate DescribeCandidate(
        Vk vk,
        PhysicalDevice physicalDevice,
        KhrSurface? surfaceApi,
        nint surfaceHandle)
    {
        PhysicalDeviceProperties properties = vk.GetPhysicalDeviceProperties(physicalDevice);

        byte* deviceNamePointer = properties.DeviceName;
        string deviceName = Encoding.UTF8.GetString(
            MemoryMarshal.CreateReadOnlySpanFromNullTerminated(deviceNamePointer));

        List<QueueFamilyDescriptor> queueFamilies = DescribeQueueFamilies(
            vk,
            physicalDevice,
            surfaceApi,
            surfaceHandle);
        string[] extensions = EnumerateDeviceExtensions(vk, physicalDevice);
        VulkanFeatureSet features = QueryFeatures(vk, physicalDevice);

        return new DeviceCandidate(
            deviceName,
            properties.DeviceType,
            properties.ApiVersion,
            properties.DriverVersion,
            queueFamilies,
            extensions,
            features);
    }

    private static List<QueueFamilyDescriptor> DescribeQueueFamilies(
        Vk vk,
        PhysicalDevice physicalDevice,
        KhrSurface? surfaceApi,
        nint surfaceHandle)
    {
        uint count = 0;
        vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, ref count, null);
        if (count == 0)
        {
            return [];
        }

        var properties = new QueueFamilyProperties[count];
        fixed (QueueFamilyProperties* pointer = properties)
        {
            vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, ref count, pointer);
        }

        var result = new List<QueueFamilyDescriptor>((int)count);
        for (uint index = 0; index < count; index++)
        {
            QueueFamilyProperties family = properties[index];
            var present = false;
            if (surfaceApi is not null)
            {
                Result supportResult = surfaceApi.GetPhysicalDeviceSurfaceSupport(
                    physicalDevice,
                    index,
                    new SurfaceKHR((ulong)surfaceHandle),
                    out Bool32 supported);
                present = supportResult == Result.Success && supported;
            }

            result.Add(
                new QueueFamilyDescriptor(
                    index,
                    family.QueueCount,
                    Graphics: (family.QueueFlags & QueueFlags.GraphicsBit) != 0,
                    Compute: (family.QueueFlags & QueueFlags.ComputeBit) != 0,
                    Transfer: (family.QueueFlags & QueueFlags.TransferBit) != 0,
                    Present: present));
        }

        return result;
    }

    private static string[] EnumerateDeviceExtensions(Vk vk, PhysicalDevice physicalDevice)
    {
        uint count = 0;
        Result result = vk.EnumerateDeviceExtensionProperties(
            physicalDevice,
            (byte*)null,
            ref count,
            null);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkEnumerateDeviceExtensionProperties failed with VkResult {result}.");
        }

        if (count == 0)
        {
            return [];
        }

        var properties = new ExtensionProperties[count];
        fixed (ExtensionProperties* pointer = properties)
        {
            result = vk.EnumerateDeviceExtensionProperties(
                physicalDevice,
                (byte*)null,
                ref count,
                pointer);
        }

        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkEnumerateDeviceExtensionProperties failed with VkResult {result}.");
        }

        var names = new string[count];
        for (var index = 0; index < count; index++)
        {
            fixed (byte* namePointer = properties[index].ExtensionName)
            {
                names[index] = Encoding.UTF8.GetString(
                    MemoryMarshal.CreateReadOnlySpanFromNullTerminated(namePointer));
            }
        }

        return names;
    }

    private static VulkanFeatureSet QueryFeatures(Vk vk, PhysicalDevice physicalDevice)
    {
        var indexing = new PhysicalDeviceDescriptorIndexingFeatures
        {
            SType = StructureType.PhysicalDeviceDescriptorIndexingFeatures,
        };
        var dynamicRendering = new PhysicalDeviceDynamicRenderingFeatures
        {
            SType = StructureType.PhysicalDeviceDynamicRenderingFeatures,
        };
        var synchronization2 = new PhysicalDeviceSynchronization2Features
        {
            SType = StructureType.PhysicalDeviceSynchronization2Features,
        };
        var features2 = new PhysicalDeviceFeatures2
        {
            SType = StructureType.PhysicalDeviceFeatures2,
            PNext = &indexing,
        };
        indexing.PNext = &dynamicRendering;
        dynamicRendering.PNext = &synchronization2;

        vk.GetPhysicalDeviceFeatures2(physicalDevice, &features2);

        return new VulkanFeatureSet(
            DynamicRendering: dynamicRendering.DynamicRendering,
            Synchronization2: synchronization2.Synchronization2,
            RuntimeDescriptorArray: indexing.RuntimeDescriptorArray,
            ShaderSampledImageArrayNonUniformIndexing:
                indexing.ShaderSampledImageArrayNonUniformIndexing,
            DescriptorBindingPartiallyBound: indexing.DescriptorBindingPartiallyBound,
            DescriptorBindingSampledImageUpdateAfterBind:
                indexing.DescriptorBindingSampledImageUpdateAfterBind,
            DescriptorBindingUpdateUnusedWhilePending:
                indexing.DescriptorBindingUpdateUnusedWhilePending,
            DescriptorBindingVariableDescriptorCount:
                indexing.DescriptorBindingVariableDescriptorCount);
    }

    private static VulkanDevice CreateDevice(
        Vk vk,
        IVulkanInstance instanceAbstraction,
        Instance instanceHandle,
        PhysicalDevice physicalDevice,
        DeviceEvaluation evaluation,
        bool requirePresent,
        ILogger logger)
    {
        QueueFamilyDescriptor queueFamily = evaluation.QueueFamily
            ?? throw new InvalidOperationException("An accepted device must have a queue family.");
        DeviceCandidate candidate = evaluation.Candidate;

        List<string> enabledExtensions = [];
        if (requirePresent)
        {
            enabledExtensions.Add(VulkanNames.SwapchainExtension);
        }

        if (Contains(candidate.Extensions, VulkanNames.DynamicRenderingExtension))
        {
            enabledExtensions.Add(VulkanNames.DynamicRenderingExtension);
        }

        if (Contains(candidate.Extensions, VulkanNames.Synchronization2Extension))
        {
            enabledExtensions.Add(VulkanNames.Synchronization2Extension);
        }

        var priority = 1f;
        var queueCreateInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = queueFamily.Index,
            QueueCount = 1,
            PQueuePriorities = &priority,
        };

        var indexing = new PhysicalDeviceDescriptorIndexingFeatures
        {
            SType = StructureType.PhysicalDeviceDescriptorIndexingFeatures,
            RuntimeDescriptorArray = true,
            ShaderSampledImageArrayNonUniformIndexing = true,
            DescriptorBindingPartiallyBound = true,
            DescriptorBindingSampledImageUpdateAfterBind = true,
            DescriptorBindingUpdateUnusedWhilePending = true,
            DescriptorBindingVariableDescriptorCount = true,
        };
        var dynamicRendering = new PhysicalDeviceDynamicRenderingFeatures
        {
            SType = StructureType.PhysicalDeviceDynamicRenderingFeatures,
            DynamicRendering = true,
        };
        var synchronization2 = new PhysicalDeviceSynchronization2Features
        {
            SType = StructureType.PhysicalDeviceSynchronization2Features,
            Synchronization2 = true,
        };
        var features2 = new PhysicalDeviceFeatures2
        {
            SType = StructureType.PhysicalDeviceFeatures2,
            PNext = &indexing,
        };
        indexing.PNext = &dynamicRendering;
        dynamicRendering.PNext = &synchronization2;

        nint extensionPointer = 0;
        Device device = default;
        try
        {
            if (enabledExtensions.Count > 0)
            {
                extensionPointer = SilkMarshal.StringArrayToPtr(
                    enabledExtensions,
                    NativeStringEncoding.UTF8);
            }

            var createInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = 1,
                PQueueCreateInfos = &queueCreateInfo,
                EnabledExtensionCount = (uint)enabledExtensions.Count,
                PpEnabledExtensionNames = extensionPointer == 0 ? null : (byte**)extensionPointer,
                PNext = &features2,
            };

            Result result = vk.CreateDevice(physicalDevice, &createInfo, null, out device);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkCreateDevice failed for {candidate.Name} with VkResult {result}.");
            }

            vk.CurrentDevice = device;
            instanceAbstraction.SetObjectName(
                device.Handle,
                ObjectType.Instance,
                instanceHandle.Handle,
                "Bork2d instance");
            instanceAbstraction.SetObjectName(
                device.Handle,
                ObjectType.Device,
                device.Handle,
                $"Bork2d device ({candidate.Name})");
            VulkanDeviceLimits limits = QueryLimits(vk, physicalDevice);
            if (limits.MaxPerStageDescriptorUpdateAfterBindSampledImages == 0)
            {
                throw new Bork2dException(
                    "The selected Vulkan device reports "
                        + "maxPerStageDescriptorUpdateAfterBindSampledImages=0, which cannot "
                        + "support the M8 bindless texture pages.");
            }

            Queue queue = vk.GetDeviceQueue(device, queueFamily.Index, 0);
            string[] finalExtensions = [.. enabledExtensions];

            logger.LogInformation(
                "Vulkan device selected: name={DeviceName}, type={DeviceType}, "
                    + "vendorId={VendorId}, deviceId={DeviceId}, apiVersion={ApiVersion}, "
                    + "driverVersion={DriverVersion}, queueFamily={QueueFamily}, "
                    + "extensions=[{Extensions}], features=[{Features}], "
                    + "maxImageDimension2D={MaxImageDimension2D}, "
                    + "maxImageArrayLayers={MaxImageArrayLayers}, "
                    + "maxColorAttachments={MaxColorAttachments}, "
                    + "maxFramebuffer={MaxFramebufferWidth}x{MaxFramebufferHeight}, "
                    + "maxPerStageDescriptorUpdateAfterBindSampledImages={MaxSampledImages}.",
                candidate.Name,
                candidate.Type,
                $"0x{GetVendorId(vk, physicalDevice):X}",
                $"0x{GetDeviceId(vk, physicalDevice):X}",
                VulkanVersion.Format(candidate.ApiVersion),
                $"0x{candidate.DriverVersion:X8}",
                queueFamily.Index,
                string.Join(", ", finalExtensions),
                DescribeFeatures(evaluation.Candidate.Features),
                limits.MaxImageDimension2D,
                limits.MaxImageArrayLayers,
                limits.MaxColorAttachments,
                limits.MaxFramebufferWidth,
                limits.MaxFramebufferHeight,
                limits.MaxPerStageDescriptorUpdateAfterBindSampledImages);

            return new VulkanDevice(
                vk,
                instanceHandle,
                physicalDevice,
                device,
                queue,
                queueFamily.Index,
                evaluation.Candidate.Features,
                limits,
                finalExtensions,
                logger);
        }
        catch
        {
            if (device.Handle != 0)
            {
                try
                {
                    vk.DestroyDevice(device, null);
                }
                catch
                {
                    // Best-effort cleanup on the device-creation failure path.
                }
            }

            vk.CurrentDevice = null;
            throw;
        }
        finally
        {
            if (extensionPointer != 0)
            {
                SilkMarshal.FreeString(extensionPointer, NativeStringEncoding.UTF8);
            }
        }
    }

    private static VulkanDeviceLimits QueryLimits(Vk vk, PhysicalDevice physicalDevice)
    {
        var indexing = new PhysicalDeviceDescriptorIndexingProperties
        {
            SType = StructureType.PhysicalDeviceDescriptorIndexingProperties,
        };
        var properties2 = new PhysicalDeviceProperties2
        {
            SType = StructureType.PhysicalDeviceProperties2,
            PNext = &indexing,
        };
        vk.GetPhysicalDeviceProperties2(physicalDevice, &properties2);
        PhysicalDeviceLimits limits = properties2.Properties.Limits;

        return new VulkanDeviceLimits(
            limits.MaxImageDimension2D,
            limits.MaxImageArrayLayers,
            limits.MaxColorAttachments,
            limits.MaxFramebufferWidth,
            limits.MaxFramebufferHeight,
            indexing.MaxPerStageDescriptorUpdateAfterBindSampledImages);
    }

    private static void LogCandidate(ILogger logger, DeviceEvaluation evaluation)
    {
        DeviceCandidate candidate = evaluation.Candidate;
        logger.LogDebug(
            "Vulkan physical device candidate: name={DeviceName}, type={DeviceType}, "
                + "apiVersion={ApiVersion}, driverVersion={DriverVersion}, "
                + "queueFamilies=[{QueueFamilies}], extensions={ExtensionCount}, "
                + "accepted={Accepted}, reason={Reason}.",
            candidate.Name,
            candidate.Type,
            VulkanVersion.Format(candidate.ApiVersion),
            $"0x{candidate.DriverVersion:X8}",
            DescribeQueueFamilies(candidate.QueueFamilies),
            candidate.Extensions.Count,
            evaluation.Accepted,
            evaluation.RejectionReason ?? "none");
    }

    private static string DescribeQueueFamilies(IReadOnlyList<QueueFamilyDescriptor> families)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < families.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            QueueFamilyDescriptor family = families[index];
            builder.Append('#')
                .Append(family.Index)
                .Append('(')
                .Append(family.QueueCount)
                .Append(" queues:");
            AppendFlag(builder, family.Graphics, "graphics");
            AppendFlag(builder, family.Compute, "compute");
            AppendFlag(builder, family.Transfer, "transfer");
            AppendFlag(builder, family.Present, "present");
            builder.Append(')');
        }

        return builder.ToString();
    }

    private static void AppendFlag(StringBuilder builder, bool enabled, string name)
    {
        if (!enabled)
        {
            return;
        }

        builder.Append(' ').Append(name);
    }

    private static string DescribeNoSuitableDevice(
        IReadOnlyList<DeviceEvaluation> evaluations,
        bool requirePresent)
    {
        var builder = new StringBuilder(
            requirePresent
                ? "No suitable Vulkan 1.3 device was found for windowed presentation. "
                : "No suitable Vulkan 1.3 device was found for offscreen rendering. ");
        for (var index = 0; index < evaluations.Count; index++)
        {
            DeviceEvaluation evaluation = evaluations[index];
            builder.Append(evaluation.Candidate.Name)
                .Append(": ")
                .Append(evaluation.RejectionReason ?? "accepted")
                .Append("; ");
        }

        return builder.ToString().TrimEnd();
    }

    private static string DescribeFeatures(VulkanFeatureSet features)
    {
        List<string> enabled =
        [
            $"dynamicRendering={features.DynamicRendering}",
            $"synchronization2={features.Synchronization2}",
            $"runtimeDescriptorArray={features.RuntimeDescriptorArray}",
            "shaderSampledImageArrayNonUniformIndexing="
                + features.ShaderSampledImageArrayNonUniformIndexing,
            $"descriptorBindingPartiallyBound={features.DescriptorBindingPartiallyBound}",
            "descriptorBindingSampledImageUpdateAfterBind="
                + features.DescriptorBindingSampledImageUpdateAfterBind,
            "descriptorBindingUpdateUnusedWhilePending="
                + features.DescriptorBindingUpdateUnusedWhilePending,
            "descriptorBindingVariableDescriptorCount="
                + features.DescriptorBindingVariableDescriptorCount,
        ];
        return string.Join(", ", enabled);
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

    private static uint GetVendorId(Vk vk, PhysicalDevice physicalDevice)
        => vk.GetPhysicalDeviceProperties(physicalDevice).VendorID;

    private static uint GetDeviceId(Vk vk, PhysicalDevice physicalDevice)
        => vk.GetPhysicalDeviceProperties(physicalDevice).DeviceID;
}

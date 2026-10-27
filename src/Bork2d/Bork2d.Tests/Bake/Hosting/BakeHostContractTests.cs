using System.Reflection;
using Bork2d.Bake.Hosting;
using Xunit;

namespace Bork2d.Tests.Bake.Hosting;

public sealed class BakeHostContractTests
{
    [Fact]
    public void HostAssemblyNameIsFixed()
    {
        Assert.Equal("Bork2d.BakeHost", BakeHostIdentity.AssemblyName);
    }

    [Fact]
    public void ManifestAttributeIsAssemblyScopedAndSingleUse()
    {
        AttributeUsageAttribute usage =
            typeof(BakeManifestAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Assembly, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);

        var attribute = new BakeManifestAttribute(typeof(string));
        Assert.Equal(typeof(string), attribute.ManifestType);
    }

    [Fact]
    public void ManifestContractHasTheGeneratedHostShape()
    {
        string[] methods = typeof(IBakeManifest)
            .GetMethods()
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "AddToPack", "Bind", "ValidateShapes" }, methods);
        Assert.Equal(
            "Types",
            Assert.Single(typeof(IBakeManifest).GetProperties()).Name);

        var registration = new BakeTypeRegistration(
            "MyGame.Data.GameConfig",
            BakeAssetKind.MessagePack);
        Assert.Equal(BakeAssetKind.MessagePack, registration.Kind);

        var site = new BakeReferenceSite(
            "items[0].icon",
            "data/icon",
            IsData: false,
            TargetTypeName: "Bork2d.Api.Assets.TextureDescriptor");
        Assert.Equal("items[0].icon", site.MemberPath);
        Assert.Equal("data/icon", site.ReferencePath);
        Assert.False(site.IsData);
        Assert.Equal("Bork2d.Api.Assets.TextureDescriptor", site.TargetTypeName);
    }
}

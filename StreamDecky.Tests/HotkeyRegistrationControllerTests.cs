using StreamDecky.Services;
using Xunit;

namespace StreamDecky.Tests;

public sealed class HotkeyRegistrationControllerTests
{
    [Fact]
    public void ReRegisterIfChanged_WhenHotkeyChanges_UnregistersThenRegisters()
    {
        var service = new RecordingHotkeyRegistrationService();
        var controller = new HotkeyRegistrationController(service);
        var host = new object();

        controller.ReRegisterIfChanged(host, 9000, 0x0002, 0x7B, 0x0001, 0x41);

        Assert.Equal(new[] { "unregister:9000", "register:9000:1:65" }, service.Operations);
    }

    [Fact]
    public void ReRegisterIfChanged_WhenHotkeyIsUnchanged_DoesNothing()
    {
        var service = new RecordingHotkeyRegistrationService();
        var controller = new HotkeyRegistrationController(service);

        controller.ReRegisterIfChanged(new object(), 9000, 0x0002, 0x7B, 0x0002, 0x7B);

        Assert.Empty(service.Operations);
    }

    private sealed class RecordingHotkeyRegistrationService : IHotkeyRegistrationService
    {
        public List<string> Operations { get; } = new();

        public void Register(object host, int id, uint modifiers, uint vk)
        {
            Operations.Add($"register:{id}:{modifiers}:{vk}");
        }

        public void Unregister(object host, int id)
        {
            Operations.Add($"unregister:{id}");
        }
    }
}
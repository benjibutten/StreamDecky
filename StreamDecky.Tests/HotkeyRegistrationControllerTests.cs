using StreamDecky.Services;
using Xunit;

namespace StreamDecky.Tests;

public sealed class HotkeyRegistrationControllerTests
{
    [Fact]
    public void ReRegister_UnregistersThenRegisters()
    {
        var service = new RecordingHotkeyRegistrationService();
        var controller = new HotkeyRegistrationController(service);
        var host = new object();

        bool registered = controller.ReRegister(host, 9000, 0x0001, 0x41);

        Assert.True(registered);
        Assert.Equal(new[] { "unregister:9000", "register:9000:1:65" }, service.Operations);
    }

    [Fact]
    public void ReRegister_WhenRegistrationFails_ReturnsFalse()
    {
        var service = new RecordingHotkeyRegistrationService { RegisterResult = false };
        var controller = new HotkeyRegistrationController(service);

        Assert.False(controller.ReRegister(new object(), 9000, 0x0002, 0x7B));
    }

    [Fact]
    public void Register_PassesThroughServiceResult()
    {
        var service = new RecordingHotkeyRegistrationService { RegisterResult = false };
        var controller = new HotkeyRegistrationController(service);

        Assert.False(controller.Register(new object(), 9000, 0x0002, 0x7B));
        Assert.Equal(new[] { "register:9000:2:123" }, service.Operations);
    }

    private sealed class RecordingHotkeyRegistrationService : IHotkeyRegistrationService
    {
        public List<string> Operations { get; } = new();

        public bool RegisterResult { get; init; } = true;

        public bool Register(object host, int id, uint modifiers, uint vk)
        {
            Operations.Add($"register:{id}:{modifiers}:{vk}");
            return RegisterResult;
        }

        public void Unregister(object host, int id)
        {
            Operations.Add($"unregister:{id}");
        }
    }
}
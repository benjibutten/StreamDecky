using System.Windows;
using StreamDecky.Helpers;

namespace StreamDecky.Services;

public interface IHotkeyRegistrationService
{
    bool Register(object host, int id, uint modifiers, uint vk);

    void Unregister(object host, int id);
}

public sealed class HotkeyRegistrationController
{
    private readonly IHotkeyRegistrationService _service;

    public HotkeyRegistrationController(IHotkeyRegistrationService? service = null)
    {
        _service = service ?? new OverlayInteropHotkeyRegistrationService();
    }

    public bool Register(object host, int id, uint modifiers, uint vk)
    {
        return _service.Register(host, id, modifiers, vk);
    }

    public void Unregister(object host, int id)
    {
        _service.Unregister(host, id);
    }

    public bool ReRegister(object host, int id, uint modifiers, uint vk)
    {
        _service.Unregister(host, id);
        return _service.Register(host, id, modifiers, vk);
    }

    private sealed class OverlayInteropHotkeyRegistrationService : IHotkeyRegistrationService
    {
        public bool Register(object host, int id, uint modifiers, uint vk)
        {
            return host is Window window
                && OverlayInterop.RegisterGlobalHotkey(window, id, modifiers, vk);
        }

        public void Unregister(object host, int id)
        {
            if (host is Window window)
                OverlayInterop.UnregisterGlobalHotkey(window, id);
        }
    }
}
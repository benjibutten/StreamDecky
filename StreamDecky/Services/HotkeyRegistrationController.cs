using System.Windows;
using StreamDecky.Helpers;

namespace StreamDecky.Services;

public interface IHotkeyRegistrationService
{
    void Register(object host, int id, uint modifiers, uint vk);

    void Unregister(object host, int id);
}

public sealed class HotkeyRegistrationController
{
    private readonly IHotkeyRegistrationService _service;

    public HotkeyRegistrationController(IHotkeyRegistrationService? service = null)
    {
        _service = service ?? new OverlayInteropHotkeyRegistrationService();
    }

    public void Register(object host, int id, uint modifiers, uint vk)
    {
        _service.Register(host, id, modifiers, vk);
    }

    public void Unregister(object host, int id)
    {
        _service.Unregister(host, id);
    }

    public void ReRegister(object host, int id, uint modifiers, uint vk)
    {
        _service.Unregister(host, id);
        _service.Register(host, id, modifiers, vk);
    }

    public void ReRegisterIfChanged(object host, int id, uint oldModifiers, uint oldVk, uint newModifiers, uint newVk)
    {
        if (oldModifiers == newModifiers && oldVk == newVk)
            return;

        ReRegister(host, id, newModifiers, newVk);
    }

    private sealed class OverlayInteropHotkeyRegistrationService : IHotkeyRegistrationService
    {
        public void Register(object host, int id, uint modifiers, uint vk)
        {
            if (host is Window window)
                OverlayInterop.RegisterGlobalHotkey(window, id, modifiers, vk);
        }

        public void Unregister(object host, int id)
        {
            if (host is Window window)
                OverlayInterop.UnregisterGlobalHotkey(window, id);
        }
    }
}
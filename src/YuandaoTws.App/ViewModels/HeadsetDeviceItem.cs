using YuandaoTws.Domain.Models;

namespace YuandaoTws.App.ViewModels;

/// <summary>设备列表项，包装领域模型用于 WPF 绑定。</summary>
public sealed class HeadsetDeviceItem
{
    public required HeadsetDevice Model { get; init; }

    public string Name => Model.Name;

    public string DetailText => Model.IsPaired
        ? $"{Model.Address} · 已配对"
        : Model.Address;
}

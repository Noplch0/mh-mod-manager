namespace MhModManager.Models;

/// <summary>
/// 组件化 MOD（bundle）内部的一个可选组件。
/// Files 为含组件前缀（c{n}/...）的存储相对路径；列表顺序即部署优先级，靠后覆盖靠前。
/// </summary>
public sealed class ModComponent
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public List<string> Files { get; set; } = [];
}

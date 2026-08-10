using System.IO;
using AllPurposeAssistant.Models;

namespace AllPurposeAssistant.Services;

public class QuickActionsService
{
    private const string FileName = "quick_actions.json";
    private readonly PersistenceService _persistence;
    private readonly List<QuickAction> _customActions = new();

    public event Action? Changed;

    public QuickActionsService(PersistenceService persistence)
    {
        _persistence = persistence;
        Load();
    }

    private void Load()
    {
        var loaded = _persistence.Load<List<QuickAction>>(FileName);
        if (loaded != null)
        {
            _customActions.AddRange(loaded);
        }
        else
        {
            // 首次运行：预置微信/QQ 作为可删的自定义项
            _customActions.Add(new QuickAction { Name = "微信", Type = ActionType.OpenApp, Target = ResolveShortcut("微信") ?? "" });
            _customActions.Add(new QuickAction { Name = "QQ", Type = ActionType.OpenApp, Target = ResolveShortcut("QQ") ?? "" });
            Save();
        }
    }

    private void Save()
    {
        try
        {
            _persistence.Save(FileName, _customActions);
        }
        catch
        {
        }
    }

    public IReadOnlyList<QuickAction> All
    {
        get
        {
            var result = new List<QuickAction>
            {
                Fixed("关机", ActionType.Shutdown),
                Fixed("重启", ActionType.Restart),
                Fixed("锁定", ActionType.Lock),
                Fixed("我的电脑", ActionType.OpenFolder, "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")
            };
            result.AddRange(_customActions);
            return result;
        }
    }

    private static QuickAction Fixed(string name, ActionType type, string target = "")
        => new() { Name = name, Type = type, Target = target, IsFixed = true };

    public void Add(string name, string target, ActionType type)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || string.IsNullOrWhiteSpace(target))
            return;

        _customActions.Add(new QuickAction
        {
            Name = trimmed,
            Type = NormalizeCustomActionType(type),
            Target = target.Trim()
        });
        Save();
        Changed?.Invoke();
    }

    public void Remove(QuickAction action)
    {
        if (action == null || action.IsFixed) return;
        _customActions.Remove(action);
        Save();
        Changed?.Invoke();
    }

    public void Move(QuickAction action, QuickAction target, bool placeAfter)
    {
        var sourceIndex = _customActions.IndexOf(action);
        var targetIndex = _customActions.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex) return;

        _customActions.RemoveAt(sourceIndex);
        if (sourceIndex < targetIndex) targetIndex--;
        if (placeAfter) targetIndex++;

        _customActions.Insert(targetIndex, action);
        Save();
        Changed?.Invoke();
    }

    public void Update(QuickAction action, string name, string target, ActionType type)
    {
        if (!_customActions.Contains(action) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(target))
            return;

        action.Name = name.Trim();
        action.Target = target.Trim();
        action.Type = NormalizeCustomActionType(type);
        Save();
        Changed?.Invoke();
    }

    private static string? ResolveShortcut(string name)
    {
        var paths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs")
        };

        foreach (var basePath in paths)
        {
            if (!Directory.Exists(basePath)) continue;
            foreach (var lnk in Directory.GetFiles(basePath, $"{name}.lnk", SearchOption.AllDirectories))
                return lnk;
        }
        return null;
    }

    private static ActionType NormalizeCustomActionType(ActionType type)
        => type is ActionType.OpenUrl or ActionType.OpenFolder ? type : ActionType.OpenApp;
}

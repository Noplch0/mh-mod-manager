using HuntForge.Models;

namespace HuntForge.Core;

public static class GroupOrder
{
    public static int Compare(ModRecord left, ModRecord right, IReadOnlyList<ModGroup> groups)
    {
        var leftGroup = groups.FirstOrDefault(group => group.Id == left.GroupId)?.Index ?? int.MaxValue;
        var rightGroup = groups.FirstOrDefault(group => group.Id == right.GroupId)?.Index ?? int.MaxValue;
        var compare = leftGroup.CompareTo(rightGroup);
        if (compare != 0)
        {
            return compare;
        }

        compare = left.Index.CompareTo(right.Index);
        return compare != 0 ? compare : left.Id.CompareTo(right.Id);
    }

    public static IEnumerable<ModRecord> Ordered(IEnumerable<ModRecord> mods, IReadOnlyList<ModGroup> groups) =>
        mods.OrderBy(mod => mod, Comparer<ModRecord>.Create((left, right) => Compare(left, right, groups)));
}

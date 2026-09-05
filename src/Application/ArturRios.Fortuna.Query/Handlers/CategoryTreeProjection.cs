using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Classification;

namespace ArturRios.Fortuna.Query.Handlers;

internal static class CategoryTreeProjection
{
    public static IReadOnlyCollection<CategoryOutput> Build(
        IReadOnlyCollection<CategoryReadSnapshot> categories,
        bool includeUsageCounts)
    {
        var nodes = categories.ToDictionary(
            category => category.Id,
            category => new MutableCategoryNode(category));
        var roots = new List<MutableCategoryNode>();

        foreach (var category in categories.OrderBy(CategoryOrder))
        {
            var node = nodes[category.Id];
            if (category.ParentId.HasValue &&
                nodes.TryGetValue(category.ParentId.Value, out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }

        var visited = new HashSet<Guid>();
        return roots
            .Select(root => root.ToOutput(includeUsageCounts, visited))
            .ToArray();
    }

    public static CategoryOutput? Find(
        IReadOnlyCollection<CategoryReadSnapshot> categories,
        Guid id,
        bool includeUsageCounts)
    {
        if (!categories.Any(category => category.Id == id))
        {
            return null;
        }

        return Find(Build(categories, includeUsageCounts), id);
    }

    private static CategoryOutput? Find(
        IEnumerable<CategoryOutput> categories,
        Guid id)
    {
        foreach (var category in categories)
        {
            if (category.Id == id)
            {
                return category;
            }

            var descendant = Find(category.Children, id);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static string CategoryOrder(CategoryReadSnapshot category) =>
        $"{category.Name.ToUpperInvariant()}\u0000{category.Id:D}";

    private sealed class MutableCategoryNode
    {
        public MutableCategoryNode(CategoryReadSnapshot category)
        {
            Category = category;
        }

        public CategoryReadSnapshot Category { get; }
        public List<MutableCategoryNode> Children { get; } = [];

        public CategoryOutput ToOutput(bool includeCounts, HashSet<Guid> visited)
        {
            if (!visited.Add(Category.Id))
            {
                return Output([], includeCounts ? Category.DirectUsageCount : null);
            }

            var children = Children
                .OrderBy(child => CategoryOrder(child.Category))
                .Select(child => child.ToOutput(includeCounts, visited))
                .ToArray();
            int? usageCount = includeCounts
                ? Category.DirectUsageCount + children.Sum(child => child.UsageCount ?? 0)
                : null;

            return Output(children, usageCount);
        }

        private CategoryOutput Output(
            IReadOnlyCollection<CategoryOutput> children,
            int? usageCount) => new()
            {
                Id = Category.Id,
                Name = Category.Name,
                ParentId = Category.ParentId,
                IsDeleted = Category.IsDeleted,
                UsageCount = usageCount,
                CreatedAt = Category.CreatedAt,
                UpdatedAt = Category.UpdatedAt,
                Children = children
            };
    }
}

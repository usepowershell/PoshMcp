using System.Reflection;

namespace PoshMcp.Foundation.Tests;

[Trait("Category", "Unit")]
public sealed class ClassificationTests
{
    private static readonly string[] Allowed = ["Unit", "Protocol", "Worker", "Pool", "Integration", "Functional", "Fault", "Soak"];

    [Fact]
    public void EveryImplementedTestHasExactlyOneKnownCategoryAndNoSkip()
    {
        var found = 0;
        foreach (var type in typeof(ClassificationTests).Assembly.GetTypes())
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var fact = method.GetCustomAttribute<FactAttribute>();
                if (fact == null) continue;
                found++;
                Assert.True(type.IsPublic && method.IsPublic, $"{type.Name}.{method.Name} is not discoverable.");
                Assert.True(string.IsNullOrEmpty(fact.Skip), $"{type.Name}.{method.Name} silently skips a required suite.");
                var categories = type.GetCustomAttributesData().Concat(method.GetCustomAttributesData())
                    .Where(t => t.AttributeType == typeof(TraitAttribute) &&
                        (string?)t.ConstructorArguments[0].Value == "Category")
                    .Select(t => (string)t.ConstructorArguments[1].Value!).ToArray();
                Validate(categories);
            }
        Assert.True(found > 0);
    }

    private static void Validate(string[] categories)
    {
        if (categories.Length != 1 || !Allowed.Contains(categories[0]))
            throw new InvalidOperationException("Every test needs exactly one recognized Category; there is no default bucket.");
    }

    [Fact]
    public void UnclassifiedUnknownOrMultiplyClassifiedTestsAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => Validate([]));
        Assert.Throws<InvalidOperationException>(() => Validate(["NewSuite"]));
        Assert.Throws<InvalidOperationException>(() => Validate(["Unit", "Worker"]));
    }
}

using System.Linq;
using EDNexus.Core.Engineering;
using Xunit;

namespace EDNexus.Tests;

public class EngineeringCatalogTests
{
    private readonly EngineeringCatalog _catalog = EngineeringCatalog.Default;

    [Fact]
    public void All_three_resources_load()
    {
        Assert.NotEmpty(_catalog.Engineers);
        Assert.NotEmpty(_catalog.Materials);
        Assert.NotEmpty(_catalog.Blueprints);
    }

    [Fact]
    public void Every_blueprint_ingredient_resolves_to_a_known_material()
    {
        foreach (var bp in _catalog.Blueprints)
        foreach (var grade in bp.Grades)
        foreach (var symbol in grade.Ingredients)
            Assert.True(_catalog.Material(symbol) is not null,
                $"Blueprint '{bp.Id}' G{grade.GradeValue} references unknown material '{symbol}'.");
    }

    [Fact]
    public void Every_blueprint_grade_references_a_known_engineer()
    {
        foreach (var bp in _catalog.Blueprints)
        foreach (var grade in bp.Grades)
        {
            Assert.NotEmpty(grade.EngineerIds);
            foreach (var id in grade.EngineerIds)
                Assert.True(_catalog.Engineer(id) is not null,
                    $"Blueprint '{bp.Id}' G{grade.GradeValue} references unknown engineer '{id}'.");
        }
    }

    [Fact]
    public void Material_categories_are_one_of_the_three_inventories()
    {
        string[] valid = { "Raw", "Manufactured", "Encoded" };
        foreach (var m in _catalog.Materials)
            Assert.Contains(m.Category, valid);
    }

    [Fact]
    public void Engineer_lookup_by_name_is_case_insensitive()
    {
        var farseer = _catalog.EngineerByName("felicity farseer");
        Assert.NotNull(farseer);
        Assert.Equal("Deciat", farseer!.System);
    }
}

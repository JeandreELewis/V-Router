using CncCad.Core.Commands;
using CncCad.Core.Documents;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;
using CncCad.Core.Selection;

namespace CncCad.Core.Tests;

public class SelectionAndCommandTests
{
    [Fact]
    public void HitTest_SelectsRectangleEdgeButNotDistantPoint()
    {
        var rectangle = new RectangleEntity { Origin = new(10, 10), Width = 100, Height = 50 };
        Assert.Same(rectangle, EntityHitTester.HitTest([rectangle], new Point2D(10.5, 30), 1));
        Assert.Null(EntityHitTester.HitTest([rectangle], new Point2D(200, 200), 1));
    }

    [Fact]
    public void Marquee_SelectsOnlyFullyContainedEntities()
    {
        var inside = new RectangleEntity { Origin = new(10, 10), Width = 20, Height = 20 };
        var crossing = new RectangleEntity { Origin = new(90, 90), Width = 20, Height = 20 };
        var ids = EntityHitTester.Inside([inside, crossing], new BoundingBox(0, 0, 100, 100)).ToArray();
        Assert.Equal(inside.Id, Assert.Single(ids));
    }

    [Fact]
    public void DeleteUndoRedo_PreservesEntityAndOrder()
    {
        var document = new CadDocument();
        var first = new RectangleEntity(); var selected = new CircleEntity(); var last = new LineEntity();
        document.Entities.AddRange([first, selected, last]);
        var history = new CommandHistory();
        history.Execute(new DeleteEntitiesCommand(document, [selected.Id]));
        Assert.Equal([first, last], document.Entities);
        history.Undo(); Assert.Equal([first, selected, last], document.Entities);
        history.Redo(); Assert.Equal([first, last], document.Entities);
    }

    [Fact]
    public void AddUndoRedo_RestoresSameEntity()
    {
        var document = new CadDocument(); var entity = new RectangleEntity(); var history = new CommandHistory();
        history.Execute(new AddEntityCommand(document, entity)); Assert.Same(entity, Assert.Single(document.Entities));
        history.Undo(); Assert.Empty(document.Entities);
        history.Redo(); Assert.Same(entity, Assert.Single(document.Entities));
    }
}

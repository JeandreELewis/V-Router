using CncCad.Core.Documents;
using CncCad.Core.Entities;

namespace CncCad.Core.Commands;

public sealed class AddEntityCommand(CadDocument document, CadEntity entity) : IUndoableCommand, IEntityAffectingCommand
{
    public string Description => $"Add {entity.GetType().Name}";
    public IReadOnlyCollection<Guid> AffectedEntityIds => [entity.Id];
    public void Execute() { if (!document.Entities.Contains(entity)) document.Entities.Add(entity); }
    public void Undo() => document.Entities.Remove(entity);
}

public sealed class DeleteEntitiesCommand : IUndoableCommand, IEntityAffectingCommand
{
    private readonly CadDocument _document;
    private readonly List<(CadEntity Entity, int Index)> _removed;
    public string Description => _removed.Count == 1 ? "Delete entity" : $"Delete {_removed.Count} entities";
    public IReadOnlyCollection<Guid> AffectedEntityIds => _removed.Select(item => item.Entity.Id).ToArray();
    public DeleteEntitiesCommand(CadDocument document, IEnumerable<Guid> ids)
    {
        _document = document;
        var selected = ids.ToHashSet();
        _removed = document.Entities.Select((entity, index) => (entity, index)).Where(item => selected.Contains(item.entity.Id)).Select(item => (item.entity, item.index)).ToList();
    }
    public void Execute() { foreach (var item in _removed) _document.Entities.Remove(item.Entity); }
    public void Undo() { foreach (var item in _removed.OrderBy(item => item.Index)) _document.Entities.Insert(Math.Min(item.Index, _document.Entities.Count), item.Entity); }
}

public sealed class AddEntitiesCommand : IUndoableCommand, IEntityAffectingCommand
{
    private readonly CadDocument _document;
    private readonly List<CadEntity> _entities;
    public string Description { get; }
    public IReadOnlyCollection<Guid> AffectedEntityIds => _entities.Select(entity => entity.Id).ToArray();
    public AddEntitiesCommand(CadDocument document, IEnumerable<CadEntity> entities, string description = "Add entities") { _document = document; _entities = entities.ToList(); Description = description; }
    public void Execute() { foreach (var entity in _entities) if (!_document.Entities.Contains(entity)) _document.Entities.Add(entity); }
    public void Undo() { foreach (var entity in _entities) _document.Entities.Remove(entity); }
}

public sealed class ImportEntitiesCommand(CadDocument document,IEnumerable<CadLayer> layers,IEnumerable<CadEntity> entities):IUndoableCommand,IEntityAffectingCommand
{
    private readonly CadLayer[] _layers=layers.ToArray();private readonly CadEntity[] _entities=entities.ToArray();private readonly Guid _before=document.ActiveLayerId;
    public string Description=>"Import vectors";public IReadOnlyCollection<Guid>AffectedEntityIds=>_entities.Select(x=>x.Id).ToArray();
    public void Execute(){foreach(var layer in _layers)if(document.Layers.All(x=>x.Id!=layer.Id))document.Layers.Add(layer);foreach(var entity in _entities)if(!document.Entities.Contains(entity))document.Entities.Add(entity);if(_layers.Length>0)document.ActiveLayerId=_layers[0].Id;}
    public void Undo(){foreach(var entity in _entities)document.Entities.Remove(entity);foreach(var layer in _layers)document.Layers.Remove(layer);document.ActiveLayerId=_before;}
}

public sealed class ReplaceImportedLayoutCommand:IUndoableCommand,IEntityAffectingCommand
{
    private readonly CadDocument _document;private readonly (CadEntity Entity,int Index)[] _oldEntities;private readonly (CadLayer Layer,int Index)[] _oldLayers;private readonly CadEntity[] _newEntities;private readonly CadLayer[] _newLayers;private readonly Guid _activeBefore;
    public ReplaceImportedLayoutCommand(CadDocument document,IEnumerable<Guid> oldEntityIds,IEnumerable<Guid> oldLayerIds,IEnumerable<CadLayer> newLayers,IEnumerable<CadEntity> newEntities)
    {
        _document=document;var entities=oldEntityIds.ToHashSet();var layers=oldLayerIds.ToHashSet();_oldEntities=document.Entities.Select((entity,index)=>(entity,index)).Where(item=>entities.Contains(item.entity.Id)).Select(item=>(item.entity,item.index)).ToArray();_oldLayers=document.Layers.Select((layer,index)=>(layer,index)).Where(item=>layers.Contains(item.layer.Id)).Select(item=>(item.layer,item.index)).ToArray();_newLayers=newLayers.ToArray();_newEntities=newEntities.ToArray();_activeBefore=document.ActiveLayerId;
    }
    public string Description=>"Replace nested layout";public IReadOnlyCollection<Guid>AffectedEntityIds=>_oldEntities.Select(item=>item.Entity.Id).Concat(_newEntities.Select(entity=>entity.Id)).Distinct().ToArray();
    public void Execute(){foreach(var item in _oldEntities)_document.Entities.Remove(item.Entity);foreach(var item in _oldLayers)_document.Layers.Remove(item.Layer);foreach(var layer in _newLayers)if(_document.Layers.All(existing=>existing.Id!=layer.Id))_document.Layers.Add(layer);foreach(var entity in _newEntities)if(!_document.Entities.Contains(entity))_document.Entities.Add(entity);if(_newLayers.Length>0)_document.ActiveLayerId=_newLayers[0].Id;}
    public void Undo(){foreach(var entity in _newEntities)_document.Entities.Remove(entity);foreach(var layer in _newLayers)_document.Layers.Remove(layer);foreach(var item in _oldLayers.OrderBy(item=>item.Index))_document.Layers.Insert(Math.Min(item.Index,_document.Layers.Count),item.Layer);foreach(var item in _oldEntities.OrderBy(item=>item.Index))_document.Entities.Insert(Math.Min(item.Index,_document.Entities.Count),item.Entity);_document.ActiveLayerId=_activeBefore;}
}

public sealed class MoveEntitiesCommand : IUndoableCommand, IEntityAffectingCommand
{
    private readonly HashSet<Guid> _ids;
    private readonly List<CadEntity> _entities;
    private readonly CncCad.Core.Geometry.Vector2D _delta;
    public MoveEntitiesCommand(CadDocument document, IEnumerable<Guid> ids, CncCad.Core.Geometry.Vector2D delta) { _ids = ids.ToHashSet(); _entities = document.Entities.Where(entity => _ids.Contains(entity.Id)).ToList(); _delta = delta; }
    public IReadOnlyCollection<Guid> AffectedEntityIds => _ids;
    public string Description => "Move entities";
    public void Execute() { foreach (var entity in _entities) EntityGeometry.Translate(entity, _delta); }
    public void Undo() { var reverse = new CncCad.Core.Geometry.Vector2D(-_delta.X, -_delta.Y); foreach (var entity in _entities) EntityGeometry.Translate(entity, reverse); }
}

public sealed class TransformEntitiesCommand : IUndoableCommand, IEntityAffectingCommand
{
    private readonly List<CadEntity> _entities;
    private readonly Dictionary<Guid, CncCad.Core.Geometry.Matrix2D> _before;
    private readonly CncCad.Core.Geometry.Matrix2D _worldTransform;
    public TransformEntitiesCommand(CadDocument document, IEnumerable<Guid> ids, CncCad.Core.Geometry.Matrix2D worldTransform, string description)
    {
        var selected=ids.ToHashSet(); _entities=document.Entities.Where(e=>selected.Contains(e.Id)).ToList(); _before=_entities.ToDictionary(e=>e.Id,e=>e.Transform); _worldTransform=worldTransform; Description=description;
    }
    public string Description { get; }
    public IReadOnlyCollection<Guid> AffectedEntityIds => _entities.Select(e=>e.Id).ToArray();
    public void Execute(){foreach(var entity in _entities)entity.Transform=CncCad.Core.Geometry.Matrix2D.Then(_before[entity.Id],_worldTransform);}
    public void Undo(){foreach(var entity in _entities)entity.Transform=_before[entity.Id];}
}

public sealed class MoveEntitiesIndividuallyCommand : IUndoableCommand, IEntityAffectingCommand
{
    private readonly Dictionary<CadEntity,CncCad.Core.Geometry.Vector2D> _moves;
    public MoveEntitiesIndividuallyCommand(IEnumerable<(CadEntity Entity,CncCad.Core.Geometry.Vector2D Delta)> moves,string description){_moves=moves.ToDictionary(x=>x.Entity,x=>x.Delta);Description=description;}
    public string Description { get; }
    public IReadOnlyCollection<Guid> AffectedEntityIds=>_moves.Keys.Select(e=>e.Id).ToArray();
    public void Execute(){foreach(var pair in _moves)EntityGeometry.Translate(pair.Key,pair.Value);}
    public void Undo(){foreach(var pair in _moves)EntityGeometry.Translate(pair.Key,new(-pair.Value.X,-pair.Value.Y));}
}

public sealed class ReplaceEntitiesCommand : IUndoableCommand, IEntityAffectingCommand
{
    private readonly CadDocument _document; private readonly List<(CadEntity Entity,int Index)> _removed; private readonly List<CadEntity> _added;
    public ReplaceEntitiesCommand(CadDocument document,IEnumerable<Guid> removeIds,IEnumerable<CadEntity> additions,string description){_document=document;var ids=removeIds.ToHashSet();_removed=document.Entities.Select((e,i)=>(e,i)).Where(x=>ids.Contains(x.e.Id)).Select(x=>(x.e,x.i)).ToList();_added=additions.ToList();Description=description;}
    public string Description{get;} public IReadOnlyCollection<Guid>AffectedEntityIds=>_removed.Count>0?_removed.Select(x=>x.Entity.Id).ToArray():_added.Select(e=>e.Id).ToArray();
    public void Execute(){foreach(var item in _removed)_document.Entities.Remove(item.Entity);foreach(var entity in _added)if(!_document.Entities.Contains(entity))_document.Entities.Add(entity);}
    public void Undo(){foreach(var entity in _added)_document.Entities.Remove(entity);foreach(var item in _removed.OrderBy(x=>x.Index))_document.Entities.Insert(Math.Min(item.Index,_document.Entities.Count),item.Entity);}
}

public sealed class CompositeCommand(IEnumerable<IUndoableCommand> commands,string description):IUndoableCommand,IEntityAffectingCommand
{
    private readonly IUndoableCommand[] _commands=commands.ToArray();public string Description{get;}=description;
    public IReadOnlyCollection<Guid>AffectedEntityIds=>_commands.OfType<IEntityAffectingCommand>().SelectMany(c=>c.AffectedEntityIds).Distinct().ToArray();
    public void Execute(){foreach(var command in _commands)command.Execute();}public void Undo(){foreach(var command in _commands.Reverse())command.Undo();}
}

public sealed class SetNodeCommand(CadEntity entity,int index,CncCad.Core.Geometry.Point2D worldPoint) : IUndoableCommand,IEntityAffectingCommand
{
    private readonly CncCad.Core.Geometry.Point2D _before=Get(entity,index); private readonly CncCad.Core.Geometry.Point2D _after=entity.Transform.Inverse().Transform(worldPoint);
    public string Description=>"Move node"; public IReadOnlyCollection<Guid>AffectedEntityIds=>[entity.Id];
    public void Execute()=>Set(entity,index,_after); public void Undo()=>Set(entity,index,_before);
    private static CncCad.Core.Geometry.Point2D Get(CadEntity e,int i)=>e switch{LineEntity l when i==0=>l.Start,LineEntity l=>l.End,PolylineEntity p=>p.Points[i],VectorPathEntity path=>VectorPathNodes.Get(path,i),_=>throw new ArgumentException("Node editing supports lines, polylines, and vector paths.")};
    private static void Set(CadEntity e,int i,CncCad.Core.Geometry.Point2D p){switch(e){case LineEntity l when i==0:l.Start=p;break;case LineEntity l:l.End=p;break;case PolylineEntity poly:poly.Points[i]=p;break;case VectorPathEntity path:VectorPathNodes.Set(path,i,p);break;}}
}

public sealed class PolylineNodesCommand(PolylineEntity entity,IEnumerable<CncCad.Core.Geometry.Point2D> points,bool isClosed,string description):IUndoableCommand,IEntityAffectingCommand
{
    private readonly List<CncCad.Core.Geometry.Point2D> _before=[..entity.Points];private readonly List<CncCad.Core.Geometry.Point2D> _after=[..points];private readonly bool _beforeClosed=entity.IsClosed;
    public string Description{get;}=description;public IReadOnlyCollection<Guid>AffectedEntityIds=>[entity.Id];
    public void Execute(){entity.Points=[.._after];entity.IsClosed=isClosed;}public void Undo(){entity.Points=[.._before];entity.IsClosed=_beforeClosed;}
}

public sealed class SetEntityColorCommand:IUndoableCommand,IEntityAffectingCommand
{
    private readonly CadEntity[] _entities;private readonly Dictionary<Guid,string?> _before;private readonly string? _after;
    public SetEntityColorCommand(CadDocument document,IEnumerable<Guid> ids,string? color){var selected=ids.ToHashSet();_entities=document.Entities.Where(x=>selected.Contains(x.Id)).ToArray();_before=_entities.ToDictionary(x=>x.Id,x=>x.StrokeColor);_after=color;}
    public string Description=>_after is null?"Use layer color":"Change vector color";public IReadOnlyCollection<Guid>AffectedEntityIds=>_entities.Select(x=>x.Id).ToArray();public void Execute(){foreach(var entity in _entities)entity.StrokeColor=_after;}public void Undo(){foreach(var entity in _entities)entity.StrokeColor=_before[entity.Id];}
}

public sealed class SetEntityGroupCommand:IUndoableCommand,IEntityAffectingCommand
{
    private readonly CadEntity[] _entities;private readonly Dictionary<Guid,Guid?> _before;private readonly Guid? _group;
    public SetEntityGroupCommand(CadDocument document,IEnumerable<Guid> ids,Guid? group){var selected=ids.ToHashSet();_entities=document.Entities.Where(x=>selected.Contains(x.Id)).ToArray();_before=_entities.ToDictionary(x=>x.Id,x=>x.GroupId);_group=group;}
    public string Description=>_group is null?"Ungroup vectors":"Group vectors";public IReadOnlyCollection<Guid>AffectedEntityIds=>_entities.Select(x=>x.Id).ToArray();public void Execute(){foreach(var entity in _entities)entity.GroupId=_group;}public void Undo(){foreach(var entity in _entities)entity.GroupId=_before[entity.Id];}
}

public sealed class EditTextEntityCommand : IUndoableCommand, IEntityAffectingCommand
{
    private readonly TextEntity _target;
    private readonly TextEntity _before;
    private readonly TextEntity _after;

    public EditTextEntityCommand(TextEntity target, TextEntity replacement)
    {
        _target = target;
        _before = (TextEntity)EntityGeometry.Clone(target);
        _after = (TextEntity)EntityGeometry.Clone(replacement);
    }

    public string Description => "Edit text";
    public IReadOnlyCollection<Guid> AffectedEntityIds => [_target.Id];
    public void Execute() => Apply(_after);
    public void Undo() => Apply(_before);

    private void Apply(TextEntity source)
    {
        _target.Text=source.Text;_target.FontFamily=source.FontFamily;_target.FontStyle=source.FontStyle;
        _target.SizeMm=source.SizeMm;_target.LetterSpacingMm=source.LetterSpacingMm;_target.LineSpacing=source.LineSpacing;
        _target.Alignment=source.Alignment;_target.FontKind=source.FontKind;_target.LayoutMode=source.LayoutMode;
        _target.PathPlacement=new(){GuideEntityId=source.PathPlacement.GuideEntityId,StartOffset=source.PathPlacement.StartOffset,BaselineOffsetMm=source.PathPlacement.BaselineOffsetMm,ReverseDirection=source.PathPlacement.ReverseDirection,FlipSide=source.PathPlacement.FlipSide,KeepUpright=source.PathPlacement.KeepUpright,FitMode=source.PathPlacement.FitMode,GuideSnapshot=[..source.PathPlacement.GuideSnapshot]};
        _target.GlyphAdvancesMm=[..source.GlyphAdvancesMm];_target.SourceOutlines=VectorPathGeometry.Clone(source.SourceOutlines);_target.Outlines=VectorPathGeometry.Clone(source.Outlines);_target.Glyphs=source.Glyphs.Select(x=>new TextGlyphGeometry{Cluster=x.Cluster,AdvanceMm=x.AdvanceMm,Order=x.Order,SourceFigures=VectorPathGeometry.Clone(x.SourceFigures),PlacedFigures=VectorPathGeometry.Clone(x.PlacedFigures)}).ToList();
    }
}

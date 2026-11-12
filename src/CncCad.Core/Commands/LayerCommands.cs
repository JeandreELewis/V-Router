using CncCad.Core.Documents;
using CncCad.Core.Entities;

namespace CncCad.Core.Commands;

public sealed class AddLayerCommand(CadDocument document,CadLayer layer):IUndoableCommand
{
    private readonly Guid _previousActive=document.ActiveLayerId;
    public string Description=>"Add layer";
    public void Execute(){if(document.Layers.All(existing=>existing.Id!=layer.Id))document.Layers.Add(layer);document.ActiveLayerId=layer.Id;}
    public void Undo(){document.Layers.Remove(layer);document.ActiveLayerId=_previousActive;}
}

public sealed class DeleteLayerCommand:IUndoableCommand
{
    private readonly CadDocument _document;private readonly CadLayer _layer;private readonly int _index;private readonly Guid _previousActive;
    public DeleteLayerCommand(CadDocument document,CadLayer layer){_document=document;_layer=layer;_index=document.Layers.IndexOf(layer);_previousActive=document.ActiveLayerId;if(_index<0)throw new ArgumentException("Layer is not in the document.",nameof(layer));if(document.Entities.Any(entity=>entity.LayerId==layer.Id))throw new InvalidOperationException("Only empty layers can be deleted.");}
    public string Description=>"Delete layer";
    public void Execute(){_document.Layers.Remove(_layer);_document.ActiveLayerId=_document.Layers[Math.Clamp(_index-1,0,_document.Layers.Count-1)].Id;}
    public void Undo(){_document.Layers.Insert(Math.Min(_index,_document.Layers.Count),_layer);_document.ActiveLayerId=_previousActive;}
}

public enum LayerBooleanProperty { Visibility, Lock }
public sealed class SetLayerBooleanCommand(CadLayer layer,LayerBooleanProperty property,bool value):IUndoableCommand
{
    private readonly bool _before=property==LayerBooleanProperty.Visibility?layer.IsVisible:layer.IsLocked;
    public string Description=>property==LayerBooleanProperty.Visibility?"Change layer visibility":"Change layer lock";
    public void Execute()=>Set(value);
    public void Undo()=>Set(_before);
    private void Set(bool state){if(property==LayerBooleanProperty.Visibility)layer.IsVisible=state;else layer.IsLocked=state;}
}

public sealed class SetLayerPropertiesCommand(CadLayer layer,string name,string color):IUndoableCommand
{
    private readonly string _beforeName=layer.Name;private readonly string _beforeColor=layer.Color;private readonly string _afterName=string.IsNullOrWhiteSpace(name)?"Layer":name.Trim();private readonly string _afterColor=color;
    public string Description=>"Edit layer";
    public void Execute(){layer.Name=_afterName;layer.Color=_afterColor;}
    public void Undo(){layer.Name=_beforeName;layer.Color=_beforeColor;}
}

public sealed class MoveEntitiesToLayerCommand:IUndoableCommand,IEntityAffectingCommand
{
    private readonly CadEntity[] _entities;private readonly Dictionary<Guid,Guid> _before;private readonly Guid _destination;
    public MoveEntitiesToLayerCommand(CadDocument document,IEnumerable<Guid> ids,Guid destination){var selected=ids.ToHashSet();_entities=document.Entities.Where(entity=>selected.Contains(entity.Id)).ToArray();_before=_entities.ToDictionary(entity=>entity.Id,entity=>entity.LayerId);_destination=destination;}
    public string Description=>"Move vectors to layer";public IReadOnlyCollection<Guid>AffectedEntityIds=>_entities.Select(entity=>entity.Id).ToArray();
    public void Execute(){foreach(var entity in _entities)entity.LayerId=_destination;}
    public void Undo(){foreach(var entity in _entities)entity.LayerId=_before[entity.Id];}
}

public sealed class ReorderLayersCommand:IUndoableCommand
{
    private readonly CadLayer[] _layers;private readonly Dictionary<Guid,int> _before;private readonly Dictionary<Guid,int> _after;
    public ReorderLayersCommand(IEnumerable<CadLayer> layers,CadLayer layer,int destinationIndex){_layers=layers.OrderBy(item=>item.Order).ToArray();_before=_layers.ToDictionary(item=>item.Id,item=>item.Order);var ordered=_layers.ToList();var source=ordered.IndexOf(layer);if(source<0)throw new ArgumentException("Layer is not in the document.",nameof(layer));ordered.RemoveAt(source);ordered.Insert(Math.Clamp(destinationIndex,0,ordered.Count),layer);_after=ordered.Select((item,index)=>(item,index)).ToDictionary(pair=>pair.item.Id,pair=>pair.index);}
    public string Description=>"Reorder layers";public void Execute()=>Apply(_after);public void Undo()=>Apply(_before);private void Apply(IReadOnlyDictionary<Guid,int> order){foreach(var layer in _layers)layer.Order=order[layer.Id];}
}

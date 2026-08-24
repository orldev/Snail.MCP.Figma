// Local styles of all four types in REST response shape (style_type, key…),
// so the caller does not need to tell sources apart.
var out = [];
function push(list, type) {
  for (var i = 0; i < list.length; i++) {
    var s = list[i];
    out.push({ key: s.key, name: s.name, style_type: type, description: s.description || '', node_id: s.id });
  }
}
push(await figma.getLocalPaintStylesAsync(), 'FILL');
push(await figma.getLocalTextStylesAsync(), 'TEXT');
push(await figma.getLocalEffectStylesAsync(), 'EFFECT');
push(await figma.getLocalGridStylesAsync(), 'GRID');
return { styles: out };

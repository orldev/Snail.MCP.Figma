// Shared subtree serializer. No optional chaining — the plugin sandbox does not support it;
// every property read sits in try/catch: getters of part nodes (detached instances, slots) throw.
var __count = 0;
function __hex(p) {
  if (!p || p.type !== 'SOLID' || !p.color) return null;
  function c(v) { return ('0' + Math.round(v * 255).toString(16)).slice(-2); }
  return '#' + c(p.color.r) + c(p.color.g) + c(p.color.b);
}
function __paints(list) {
  if (!Array.isArray(list)) return undefined;
  var out = [];
  for (var i = 0; i < list.length; i++) {
    var p = list[i];
    if (p.visible === false) continue;
    out.push(__hex(p) || p.type);
  }
  return out.length ? out : undefined;
}
function __ser(node, depth, maxDepth, maxNodes) {
  if (__count >= maxNodes) return null;
  __count++;
  var o = { id: node.id, name: node.name, type: node.type };
  if (node.visible === false) o.visible = false;
  try { if (typeof node.x === 'number') { o.x = Math.round(node.x); o.y = Math.round(node.y); } } catch (e) {}
  try { if (typeof node.width === 'number') { o.width = Math.round(node.width); o.height = Math.round(node.height); } } catch (e) {}
  try { if (node.type === 'TEXT') o.characters = String(node.characters).slice(0, 400); } catch (e) {}
  try { var f = __paints(node.fills); if (f) o.fills = f; } catch (e) {}
  try { var s = __paints(node.strokes); if (s) o.strokes = s; } catch (e) {}
  try { if (typeof node.cornerRadius === 'number' && node.cornerRadius) o.cornerRadius = node.cornerRadius; } catch (e) {}
  try { if (typeof node.opacity === 'number' && node.opacity !== 1) o.opacity = node.opacity; } catch (e) {}
  try {
    if (node.layoutMode && node.layoutMode !== 'NONE') {
      o.layout = {
        mode: node.layoutMode,
        itemSpacing: node.itemSpacing,
        padding: [node.paddingTop, node.paddingRight, node.paddingBottom, node.paddingLeft]
      };
    }
  } catch (e) {}
  try { if (node.description) o.description = node.description; } catch (e) {}
  try { if (node.componentPropertyDefinitions) o.componentPropertyDefinitions = node.componentPropertyDefinitions; } catch (e) {}
  try { if (node.variantProperties) o.variantProperties = node.variantProperties; } catch (e) {}
  try { if (node.mainComponent) o.mainComponentId = node.mainComponent.id; } catch (e) {}
  if (depth < maxDepth && node.children && node.children.length) {
    var kids = [];
    for (var i = 0; i < node.children.length && __count < maxNodes; i++) {
      var k = __ser(node.children[i], depth + 1, maxDepth, maxNodes);
      if (k) kids.push(k);
    }
    if (kids.length) o.children = kids;
    if (kids.length < node.children.length) o.childrenTruncated = node.children.length - kids.length;
  } else if (node.children && node.children.length) {
    o.childCount = node.children.length;
  }
  return o;
}

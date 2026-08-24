// Subtree of one node. The id arrives as a JSON literal (__NODE_ID__), not by concatenation:
// instance sub-layer ids (I1:2;3:4) are arbitrary strings and must stay data.
var __id = __NODE_ID__;
var __node = await figma.getNodeByIdAsync(__id);
if (!__node) return { found: false, nodeId: __id };
return { found: true, node: __ser(__node, 0, __DEPTH__, __MAX_NODES__), nodeCount: __count };

// File tree from the root. depth follows REST semantics: 1 means pages only.
// Pages are loaded only when going deeper than them — noticeable on large files.
if (__DEPTH__ > 1) { await figma.loadAllPagesAsync(); }
return { found: true, node: __ser(figma.root, 0, __DEPTH__, __MAX_NODES__), nodeCount: __count };

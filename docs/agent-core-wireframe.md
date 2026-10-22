# Agent Core wireframe query

The latest `AgentCoreSnapshot` can be passed to the on-demand
`NavigationControllerRegistry.QueryWireframe` method. It is read-only and does
not run during the ordinary per-tick observation loop. The output is one
self-contained SVG with three orthographic panels:

- **Top:** Agent Core right vs. forward.
- **Side:** Agent Core forward vs. up.
- **Front:** Agent Core right vs. up.

Coordinates are relative to the Agent Core's captured right/up/forward frame,
not world GPS. Each discovered block is drawn as the projected hull of its eight
occupied-volume corners. The corners are derived on demand from the block's
captured world center and occupied grid bounds, the grid cell size, and the
captured physical-subgrid transform, then converted into the core-local frame.
This preserves the location of rotor- and piston-mounted physical subgrids
instead of laying every grid over the root grid's origin, without storing eight
extra points per block in every periodic snapshot.

Color selections are optional. Each selector can constrain grid entity ID,
block entity ID, definition type, and/or subtype ID; all supplied fields must
match, and the first matching selector wins. IDs set to zero and empty strings
are wildcards, but a selector cannot be entirely wildcard. Colors must be
opaque `#RRGGBB`. The normal body outline is muted blue-gray; selected blocks
receive the requested stroke color, stronger line weight, and a translucent
fill. A block may be selected by entity identity or by definition/subtype when
it has no entity ID.

The renderer is bounded: 128 selectors, 4,096 rendered blocks, and a 1.5-million
character SVG ceiling. Highlighted blocks are prioritized if the geometry must
be truncated. The result reports rendered/selected counts and truncation; a
truncated image must not be treated as a complete body diagram. Malformed block
geometry is omitted and also marks the result truncated. Metadata is XML-escaped
and selector colors are validated before being written into SVG.

This is the query/rendering primitive used by the local stdio adapter described
in [agent-core-mcp.md](agent-core-mcp.md). The adapter reads a world-scoped
snapshot export and returns this SVG as a bounded MCP embedded resource when
`includeWireframe` is requested. It does not gain access to live game APIs,
player identity, or control operations. The renderer accepts only a captured
snapshot and selection rules; it cannot issue game commands.

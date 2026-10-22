# V3 gravity-referenced sensor trial

This record covers server-side, physics-ray sensing on the disposable V3
planetary fixture. It did not modify the source save. The sensor produces
depth, hit-entity IDs, surface normals, and natural-gravity reference metadata;
it is not a renderer or an autonomous-navigation proof.

## Surface orientation

Eight 32×18, 50 m, 90° captures from the red and blue camera rigs returned
2,281 hit rays. All 2,281 had a valid normal; none hit the camera's own grid.
Using each capture's measured local up, the normals binned as 2,217
floor-facing, 56 wall-like, 4 slope-like, and 4 down-facing. Local up was
approximately `[0.857, 0.100, -0.506]`, nearly perpendicular to global Y.

## Red-front horizontal-FOV sweep

The same camera ID (`109088616374925990`), logged origin, and forward direction
were used at 64×36, 250 m maximum range. Captures were sequential, but no
vehicle movement command was issued between them.

| Horizontal FOV | Hit rays | Coverage | Named drone pixels | Incidental wheel-grid pixels |
| ---: | ---: | ---: | ---: | ---: |
| 80° | 2,193 / 2,304 | 95.2% | 0 | 0 |
| 100° | 1,974 / 2,304 | 85.7% | 68 | 4 |
| 120° | 1,758 / 2,304 | 76.3% | 112 | 30 |

This suggests a useful early tradeoff: narrower FOV gives more hit samples per
angular area, while wider FOV includes more peripheral objects at this fixed
resolution. It is only a three-point trial in one fixture; hit coverage is not
accuracy, completeness, or a safe-path score.

The same sweep was repeated on the blue-front camera at the same resolution
and range. Its coverage moved in the opposite direction—997/2,304 (43.3%) at
80°, 1,092/2,304 (47.4%) at 100°, and 1,238/2,304 (53.7%) at 120°—while all
returns remained terrain. Camera pose and direction were unchanged within
logged precision in this sweep too. The opposite trends are a useful warning:
hit density depends on scene geometry and view direction, so FOV should be
selected against the task rather than inferred from a universal density rule.

## Raster-resolution check

The Red-front 100°/250 m sample was repeated at two lower resolutions. Camera
origin/direction matched within logged precision across the captures.

| Raster | Hit rays | Coverage | Drone pixels | Wheel-grid pixels |
| ---: | ---: | ---: | ---: | ---: |
| 16×9 | 122 / 144 | 84.7% | 5 | 0 |
| 32×18 | 496 / 576 | 86.1% | 17 | 1 |
| 64×36 | 1,974 / 2,304 | 85.7% | 68 | 4 |

Coverage stayed nearly unchanged while raster area scaled 1:4:16. The drone
pixel counts scaled similarly; this suggests low-resolution frames may preserve
coarse hit coverage at lower sampling cost in this view. It is a one-view
check, not evidence of linear scaling across other scenes. As an extra check,
each low-resolution ray was compared with the nearest center-adjacent ray in
the 64×36 raster (the ray centers do not coincide exactly). Hit/miss agreement
was 99.3% for 16×9 and 98.1–99.0% for 32×18 depending on the adjacent-pixel
choice. For pairs that both hit, median absolute depth difference was about
0.66–0.88 m and 1.06–1.08 m respectively. This indicates that coarse frames
can preserve occupancy here, while their individual depth samples remain
meter-scale approximations on the sloped scene.

The Blue-front 100° view showed the same coarse-coverage pattern: 16×9, 32×18,
and 64×36 hit 70/144 (48.6%), 271/576 (47.0%), and 1,092/2,304 (47.4%). Its
nearest-center hit/miss agreement against the high-resolution raster was
96.5–98.6% for 16×9 and 97.7–98.4% for 32×18, with about 0.54–0.60 m median
depth difference among jointly hit samples. Thus the coarse-coverage result
appears in both views here, while exact depth and edge detail still depend on
sampling density.

## Receipt references

All sensor artifact hashes and run receipts were validated by VRageCage. Each
run passed depth, entity-ID, entity-summary, surface-normal, and local-gravity
checks; clean shutdown completed with no target-fatal markers.

| FOV | Operation ID | Run ID | Depth SHA-256 | Entity IDs SHA-256 | Normals SHA-256 | Metadata SHA-256 |
| ---: | --- | --- | --- | --- | --- | --- |
| 80° | `42c5e8d2a801681062749733fb00df15` | `a7869c9dcb8f09250a33e261f8151808` | `7ea020dec8531e94e1baebf0c10bf77170289cb7c80759a29a28c124d0d6a674` | `601fc0f7e7b37c9da56f31507337b192ccd841d1da2fee27378dd20dc66ce4b1` | `c0ae87107e3a4784b63f49a42d3d559229b9b6361136bc3e7b1f060ede673ab3` | `f283be5d8a93b8d99d90e82ba890022d97b49eb7f2e6b5a041fa0f302b9ecff4` |
| 100° | `139f4f83f87a175459dfada8aaa379d1` | `cb6f0c32b51c40ac4f379b308235c1ec` | `05b90ee255523f27b12efce91405c658f99a1973a224987776ac6dc5fe634454` | `8763548dc7d85d8ccf438d0cb605769eb9f5f5124543f7d3037bb642b0c0e648` | `f9eb5c01a2000501644c75e4691185edae107492b504d5f92ed2a58cfcb54566` | `c2a5cee07289e50aa2745823a198038a5fd96e82ae3fd2572f94a272decac18d` |
| 120° | `092380d08f2f23e198c1e4be1de8b6c5` | `7794176a481e1ea17e34e827458cc6e7` | `36bf49895bc7c3fcb2898b2504c1f82bdee947cbaf8c2d4580f39a676411d8f0` | `64ca1761feba5942f75a7cd27c821a6f575e41724b2a8fa72af108e2adb8d518` | `9ae296a60736f61888c07edfc8a4c9d934d7d4d0c8f37aaf5daeb16464d156c1` | `612404ebae516f9fc0076b86de4cd81a61df3347e4ed84946ff9285d531fcf6f` |

The blue-front 80° and 120° captures are also receipt-validated. Their
operation IDs are `bba94ae1f47321cfe73eadfe36ffb225` and
`84b86538066b542cd2489c7ad7f7ae2c`; full hashes and metadata are in the
machine-readable survey record. Blue-front 80° hit 997/2,304 rays (43.3%),
and 120° hit 1,238/2,304 (53.7%). The Blue-front resolution checks used
operations `4c1dc9cd99ab7bf32f5a928e81a72de9` (16×9) and
`7ae88571f62272e8faf68f7f97f767aa` (32×18), with runs
`63a9182e799c07d84952fbf46e13f4b3` and
`77bf0c61218143ad2695580dda5edde4`. The Red-front resolution captures used
operations `bf589efab5c034e396e91b166f11afc4` (16×9) and
`b0b1cea8ec2374fe9f632a74c298ca74` (32×18); their runs were
`a729ce6dc417347b157571524d553441` and
`fe7e11c4345891f66d20f02557521d29`. Full hashes are in the machine-readable
survey record.

The final clone shutdown saved a changed sector hash (`09386b4c4ce9ce1b2469faf7718589b5abdca5fcd15fc610d4ad997cf8cb3e03`); earlier in the same experiment it was `c9e7e23c84f00371698ce513f9108c72af2b6d43ad5385230397bb2a092d2106`. These writes were confined to the disposable clone. The original source world was not touched.

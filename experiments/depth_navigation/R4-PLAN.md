# Preregistered fourth experiment: supplied geometry and supported starts

This plan is written before executing the fourth frozen candidate. Earlier r3
test sets are spent development evidence; r4 uses new seeds and retains its own
untouched calibration/evaluation scenes. No threshold is selected on test data.

## Motivation from the completed experiments

The front-only r3 CNNs permitted roughly 6–11% of queries. In the first three-seed
comparison, about 11–22% of their permitted queries were hazardous under the
full-geometry oracle. The lower hazard-miss rate alone therefore did not establish
an acceptable permission policy. Randomly thinning the geometry baseline's
permissions to the same coverage explained much of the apparent improvement.
Multi-camera CNN results also remained inadequate. Numeric versus visual-strip
direction did not have a consistent winner across seeds.

An exact-source audit of r3 seed 47001 found 140 of 543 hazardous test queries
already had a hazardous query-aligned footprint at distance zero; these accounted
for 8 of 17 false-safe predictions. This is an oracle counterfactual, not a
measurement of a current game vehicle. A front camera can be unable to observe
such conditions. Separately, an executable counterexample has identical front
depth/conditioning for a clear rear query and an unseen rear wall. No model can
recover missing information from identical inputs.

## Predeclared changes and controls

1. Reject a generated scene when its **query-aligned footprint at distance zero**
   is hazardous. This specifies a new supported-start data distribution, not a
   retroactive relabeling or improvement claim on r3. It is still a simplified
   geometric query, not a steering arc or a simulation of wheel physics.
2. Keep 32x18 complete camera frames, four fixed quadrant roles, explicit masks,
   independent CPU oracle, randomized poses/vehicle dimensions, and held-out
   boulder/rough families. A clear frustum without supporting-surface depth hits
   now abstains; max-distance space cannot manufacture a floor.
3. Compare the existing raw CNN against a tiny shared per-point network. Its
   deterministic preprocessor uses **only depth pixels and the supplied vehicle,
   camera and numeric direction tensors** to reconstruct points and finite-
   difference normals. No oracle normal, terrain family or world geometry enters
   the model. Shared point weights operate across available camera roles.
4. Numeric direction is fixed for this geometry-conditioning comparison.
   The independent r3 strip comparison remains preserved. This is not a claim
   that numeric encoding is generally superior.
5. Use seeds 48001/48002/48003; 20,000 training, 2,500 calibration and 2,500 per
   evaluation split; 150 epochs; same splits for both architectures. Run front-
   only and randomized-camera loadouts as separate bounded campaigns.
6. Select thresholds on validation only, requiring empirical hazard-miss rate
   <=2%, unsafe fraction among permissions <=2%, and at least 50 permissions.
   If no threshold qualifies, threshold zero permits nothing. Report both
   denominators, counts, coverage and descriptive Wilson intervals. These
   empirical constraints do not certify physical safety or statistical coverage.
7. Keep the conditioning-only, geometry and always-stop evidence from r3; r4
   includes the geometry and always-stop baselines on its own new scenes. Compare
   at matched coverage to avoid mistaking abstention for added information.

## Required checks and acceptance

- CPU point reprojection matches independent NumPy camera rays.
- Missing cameras and no-hit-only queries cannot receive projected-model
  permission; finite nonzero parameter gradients precede expensive generation.
- GPU renderer agrees with the CPU renderer; labels remain independently sampled.
- Inspect actual test images and failures, paired split identities and mid-run
  camera-loss counterfactuals. Report failed qualification and zero coverage.
- Each run uses the existing authenticated Roost lease, fixed hashed source and
  bounded secret-free unit. Cancellation and terminal lease release remain gates.
- No checkpoint can be deployed or used for live vehicle control from this plan.
  Recorded game frames without timing and controller bindings remain inspection-
  only, even when their hashes and PGM encoding validate.

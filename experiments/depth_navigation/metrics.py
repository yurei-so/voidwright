"""Decision metrics with explicit eligibility and descriptive uncertainty."""
import math
import numpy as np


def wilson(successes, count, z=1.96):
    if not count:
        return [0., 1.]
    proportion = successes / count
    denominator = 1 + z * z / count
    center = (proportion + z * z / (2 * count)) / denominator
    radius = z * math.sqrt(proportion * (1 - proportion) / count + z * z / (4 * count * count)) / denominator
    return [max(0., center - radius), min(1., center + radius)]


def metrics(labels, probabilities, threshold, eligible=None):
    labels = np.asarray(labels) > .5
    probabilities = np.asarray(probabilities)
    eligible = np.ones(len(labels), bool) if eligible is None else np.asarray(eligible, bool)
    if probabilities.shape != labels.shape or eligible.shape != labels.shape or not np.isfinite(probabilities).all():
        raise ValueError("Malformed prediction/eligibility vector")
    safe = (probabilities < threshold) & eligible
    hazards = int(labels.sum()); false_safe = int((safe & labels).sum())
    eligible_hazards = int((eligible & labels).sum())
    permissions=int(safe.sum())
    return dict(samples=len(labels), hazardous=hazards, safe=int((~labels).sum()),
                false_safe=false_safe, false_safe_rate=false_safe / max(1, hazards),
                false_safe_rate_wilson95=wilson(false_safe, hazards), eligible_hazardous=eligible_hazards,
                false_safe_rate_when_eligible=false_safe / max(1, eligible_hazards),
                safe_predictions=int(safe.sum()), coverage=float(safe.mean()) if len(safe) else 0.,
                unsafe_per_permission=false_safe/permissions if permissions else None,
                unsafe_per_permission_wilson95=wilson(false_safe,permissions),
                clear_recall=float((safe & ~labels).sum() / max(1, (~labels).sum())),
                threshold=float(threshold), eligible=int(eligible.sum()))


def calibrate(labels, probabilities, maximum_false_safe=.02, eligible=None,maximum_permission_error=None,minimum_permissions=0):
    """Validation only, same eligible policy as evaluation; empirical limit."""
    if not (np.asarray(labels) > .5).any():
        raise ValueError("Calibration requires hazardous examples")
    candidates = np.linspace(0, 1, 201)
    feasible=[]
    for threshold in candidates:
        result=metrics(labels,probabilities,threshold,eligible)
        if result['false_safe_rate']>maximum_false_safe or result['safe_predictions']<minimum_permissions:continue
        if maximum_permission_error is not None and (result['unsafe_per_permission']is None or result['unsafe_per_permission']>maximum_permission_error):continue
        feasible.append(threshold)
    return float(max(feasible, default=0.))

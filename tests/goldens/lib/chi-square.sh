#!/bin/bash
# Chi-square goodness-of-fit test for a single binary outcome.
#
# Usage:
#   chi-square.sh <observed_count> <total> <expected_rate> <significance>
#
# Arguments:
#   observed_count  Number of times the event was observed (integer).
#   total           Total number of trials (integer).
#   expected_rate   Expected probability of the event (decimal, e.g. 0.20).
#   significance    Significance level for the test (decimal, e.g. 0.01).
#
# Exit codes:
#   0  p >= significance (fail to reject H0 — distribution is as expected)
#   1  p < significance  (reject H0 — distribution differs significantly)
#   2  Usage error or invalid inputs
#
# The test uses the chi-square statistic X² = (O-E)²/E + (O'-E')²/E'
# where O is the observed count, E is the expected count under H0, and
# O'/E' are the complementary cell values.  The p-value is approximated
# from the chi-square CDF with df=1 using the Wilson-Hilferty cube-root
# approximation, which is accurate to within a few percent for X² > 0.

set -euo pipefail

if [[ $# -ne 4 ]]; then
    echo "Usage: chi-square.sh <observed_count> <total> <expected_rate> <significance>" >&2
    exit 2
fi

OBSERVED="$1"
TOTAL="$2"
EXPECTED_RATE="$3"
SIGNIFICANCE="$4"

# Delegate to awk for floating-point arithmetic.
awk -v obs="$OBSERVED" -v total="$TOTAL" -v rate="$EXPECTED_RATE" -v sig="$SIGNIFICANCE" '
BEGIN {
    # Validate inputs
    if (total <= 0) { print "ERROR: total must be > 0" > "/dev/stderr"; exit 2 }
    if (rate < 0 || rate > 1) { print "ERROR: expected_rate must be in [0,1]" > "/dev/stderr"; exit 2 }
    if (sig <= 0 || sig >= 1) { print "ERROR: significance must be in (0,1)" > "/dev/stderr"; exit 2 }

    # Expected counts under H0
    e1 = total * rate
    e2 = total * (1 - rate)

    # Observed complement
    obs2 = total - obs

    # Avoid division by zero for edge rates
    chi2 = 0
    if (e1 > 0) chi2 += (obs  - e1)^2 / e1
    if (e2 > 0) chi2 += (obs2 - e2)^2 / e2

    # Wilson-Hilferty approximation for chi-square(df=1) p-value
    # p = P(X >= chi2) where X ~ chi²(1)
    # z = ((chi2/1)^(1/3) - (1 - 2/9)) / sqrt(2/9)
    # p ≈ 1 - Phi(z)  (standard normal CDF)
    if (chi2 <= 0) {
        p = 1.0
    } else {
        k = 1  # degrees of freedom
        z = ((chi2/k)^(1.0/3) - (1 - 2.0/(9*k))) / sqrt(2.0/(9*k))
        # Approximation of 1 - Phi(z) using Abramowitz & Stegun 26.2.17
        if (z >= 0) {
            t = 1 / (1 + 0.2316419 * z)
            poly = t*(0.319381530 + t*(-0.356563782 + t*(1.781477937 + t*(-1.821255978 + t*1.330274429))))
            phi_z = 1 - (1/sqrt(2*atan2(0,-1))) * exp(-z*z/2) * poly
            p = 1 - phi_z
        } else {
            # z < 0: p is large (fail to reject)
            p = 1.0
        }
    }

    printf "chi2=%.4f  p=%.6f  obs=%d  exp=%.1f  total=%d  rate=%s  sig=%s\n", \
        chi2, p, obs, e1, total, rate, sig
    if (p >= sig) {
        exit 0
    } else {
        exit 1
    }
}
'

#ifndef ANOMALY_HLSLI
#define ANOMALY_HLSLI

// Present so Anomaly's include directory is a real Keen include root.
// Nothing in Keen's tree includes this file yet. Later GBuffer stages will
// #include <Anomaly.hlsli> under #ifdef ANOMALY_VELOCITY.
//
// ANOMALY is defined by the compile intercept (always, once live).
// ANOMALY_VELOCITY is a later permutation define (GBuffer only).

#endif

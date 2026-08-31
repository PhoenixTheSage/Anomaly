Pulsar copies this folder and calls Plugin.LoadAssets with its path.

Shaders/ is Keen’s extra include directory (system #include <Anomaly.hlsli>)
and the runtime HLSL for owned passes (Fullscreen.hlsl, CameraVelocity.hlsl).
Deploy.bat also copies Shaders next to the Local DLL as a fallback.

Anomaly does not ship NVIDIA redistributables.

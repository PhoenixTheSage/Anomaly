#ifndef ANOMALY_GBUFFER_ATTACHMENT_INIT_HLSLI
#define ANOMALY_GBUFFER_ATTACHMENT_INIT_HLSLI

// Disk fallback when generated extras are not served. Runtime generation
// prepends output = (GbufferOutput)0 and extra-attachment zeros.
void AnomalyInitAttachments(inout GbufferOutput output)
{
    output = (GbufferOutput)0;
}

#endif

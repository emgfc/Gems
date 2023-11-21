// Licensed to the Hoff Tech under one or more agreements.
// The Hoff Tech licenses this file to you under the MIT license.

namespace Gems.OpenTelemetry.Configuration;

public class TracingConfigurationCommand
{
    public bool? IncludeRequest { get; set; }

    public bool? IncludeResponse { get; set; }
}

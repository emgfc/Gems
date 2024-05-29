﻿// Licensed to the Hoff Tech under one or more agreements.
// The Hoff Tech licenses this file to you under the MIT license.

using Gems.Metrics.Contracts;

namespace Gems.Metrics.Samples.Operations.Counter.Persons.CreatePerson
{
    public enum CreatePersonMetricType
    {
        [Metric(
            Name = "persons_created",
            Description = "Количество созданных персон")]
        PersonsCreated
    }
}

﻿// Licensed to the Hoff Tech under one or more agreements.
// The Hoff Tech licenses this file to you under the MIT license.

using Gems.Metrics.Samples.Operations.Counter.Persons.CreatePerson.Dto;

using MediatR;

namespace Gems.Metrics.Samples.Operations.Counter.Persons.CreatePerson
{
    public record CreatePersonCommand : IRequest<PersonDto>
    {
        public string FirstName { get; init; }

        public string LastName { get; init; }

        public int Age { get; init; }

        public Gender Gender { get; init; }
    }
}

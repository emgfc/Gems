// Licensed to the Hoff Tech under one or more agreements.
// The Hoff Tech licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Gems.Jobs.Quartz.Configuration;
using Gems.Jobs.Quartz.Handlers.Consts;
using Gems.Jobs.Quartz.Handlers.Shared;
using Gems.Mvc.GenericControllers;

using MediatR;

using Microsoft.Extensions.Options;

using Quartz;

namespace Gems.Jobs.Quartz.Handlers.ScheduleJob
{
    [Endpoint("jobs/{JobName}", "POST", OperationGroup = "jobs")]
    public class ScheduleJobCommandHandler : IRequestHandler<ScheduleJobCommand>
    {
        private readonly IOptions<JobsOptions> options;
        private readonly SchedulerProvider schedulerProvider;
        private readonly TriggerHelper triggerHelper;

        public ScheduleJobCommandHandler(
            IOptions<JobsOptions> options,
            SchedulerProvider schedulerProvider,
            TriggerHelper triggerHelper)
        {
            this.options = options;
            this.schedulerProvider = schedulerProvider;
            this.triggerHelper = triggerHelper;
        }

        public async Task Handle(ScheduleJobCommand request, CancellationToken cancellationToken)
        {
            var scheduler = await this.schedulerProvider.GetSchedulerAsync(cancellationToken).ConfigureAwait(false);
            var trigger = await scheduler
                .GetTrigger(
                    new TriggerKey(request.JobName, request.JobGroup ?? JobGroups.DefaultGroup),
                    cancellationToken)
                .ConfigureAwait(false);

            if (trigger != null)
            {
                throw new InvalidOperationException($"Такое задание уже зарегистрировано {request.JobGroup ?? JobGroups.DefaultGroup}.{request.JobName}");
            }

            if (await this.ScheduleSimpleTrigger(scheduler, request.JobName, request.JobGroup, request.CronExpression, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            if (await this.ScheduleTriggerWithData(scheduler, request.JobName, request.JobGroup, request.CronExpression, request.TriggerName, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            await this.ScheduleTriggerFromDb(scheduler, request.JobName, request.JobGroup, request.CronExpression, request.TriggerName, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<List<string>> GetTriggersForSchedule(IScheduler scheduler, string jobName, List<string> triggersFromConfiguration, CancellationToken cancellationToken)
        {
            var jobTriggers = (await scheduler.GetTriggersOfJob(new JobKey(jobName), cancellationToken).ConfigureAwait(false)).ToList();
            return triggersFromConfiguration.Where(triggerName => !jobTriggers.Exists(t => t.Key.Name == triggerName)).ToList();
        }

        private async Task<bool> ScheduleSimpleTrigger(IScheduler scheduler, string jobName, string jobGroup, string cronExpression, CancellationToken cancellationToken)
        {
            if (this.options.Value.Triggers == null || !this.options.Value.Triggers.ContainsKey(jobName))
            {
                return false;
            }

            var triggerCronExpression = this.options.Value.Triggers
                .Where(r => r.Key == jobName)
                .Select(r => r.Value)
                .First();

            var newTrigger = this.triggerHelper.CreateCronTrigger(
                jobName,
                jobGroup ?? JobGroups.DefaultGroup,
                jobName,
                jobGroup ?? JobGroups.DefaultGroup,
                cronExpression ?? triggerCronExpression,
                null);

            await scheduler.ScheduleJob(newTrigger, cancellationToken).ConfigureAwait(false);
            return true;
        }

        private async Task<bool> ScheduleTriggerWithData(
            IScheduler scheduler,
            string jobName,
            string jobGroup,
            string cronExpression,
            string triggerName,
            CancellationToken cancellationToken)
        {
            if (this.options.Value.TriggersWithData == null || !this.options.Value.TriggersWithData.ContainsKey(jobName))
            {
                return false;
            }

            if (string.IsNullOrEmpty(triggerName))
            {
                return await this.ScheduleTriggersWithData(scheduler, jobName, jobGroup, cronExpression, cancellationToken).ConfigureAwait(false);
            }

            var triggerFromConf = this.options.Value.TriggersWithData
                .GetValueOrDefault(jobName)
                .ToList()
                .First(t => t.TriggerName == triggerName);

            var trigger = this.triggerHelper.CreateCronTrigger(
                triggerFromConf.TriggerName ?? jobName,
                jobGroup ?? JobGroups.DefaultGroup,
                jobName,
                jobGroup ?? JobGroups.DefaultGroup,
                cronExpression ?? triggerFromConf.CronExpression,
                triggerFromConf.TriggerData);
            await scheduler.ScheduleJob(trigger, cancellationToken).ConfigureAwait(false);
            return true;
        }

        private async Task<bool> ScheduleTriggersWithData(IScheduler scheduler, string jobName, string jobGroup, string cronExpression, CancellationToken cancellationToken)
        {
            var triggersFromConfiguration = new List<string>();
            foreach (var triggerWithData in this.options.Value.TriggersWithData.Where(t => t.Key == jobName).Select(t => t.Value))
            {
                triggersFromConfiguration.AddRange(triggerWithData.Select(t => t.TriggerName));
            }

            var triggersForSchedule = await GetTriggersForSchedule(scheduler, jobName, triggersFromConfiguration, cancellationToken).ConfigureAwait(false);

            foreach (var triggerNameForSchedule in triggersForSchedule)
            {
                await this.ScheduleTriggerWithData(scheduler, jobName, jobGroup, cronExpression, triggerNameForSchedule, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }

        private async Task ScheduleTriggerFromDb(
            IScheduler scheduler,
            string jobName,
            string jobGroup,
            string cronExpression,
            string triggerName,
            CancellationToken cancellationToken)
        {
            if (this.options.Value.TriggersFromDb == null || !this.options.Value.TriggersFromDb.ContainsKey(jobName))
            {
                return;
            }

            if (string.IsNullOrEmpty(triggerName))
            {
                await this.ScheduleTriggersFromDb(scheduler, jobName, jobGroup, cronExpression, cancellationToken).ConfigureAwait(false);
                return;
            }

            var triggerFromDb = this.options.Value.TriggersFromDb
                .GetValueOrDefault(jobName)
                .ToList()
                .First(t => t.TriggerName == triggerName);

            var triggerProviderType = this.triggerHelper.GetTriggerDbType(triggerFromDb);
            var triggerCronExpression = await triggerProviderType.GetCronExpression(triggerFromDb.TriggerName, cancellationToken).ConfigureAwait(false);
            var triggerDataDict = await triggerProviderType.GetTriggerData(triggerFromDb.TriggerName, cancellationToken).ConfigureAwait(false);

            var trigger = this.triggerHelper.CreateCronTrigger(
                triggerFromDb.TriggerName ?? jobName,
                jobGroup ?? JobGroups.DefaultGroup,
                jobName,
                jobGroup ?? JobGroups.DefaultGroup,
                cronExpression ?? triggerCronExpression,
                triggerDataDict);
            await scheduler.ScheduleJob(trigger, cancellationToken).ConfigureAwait(false);
        }

        private async Task ScheduleTriggersFromDb(IScheduler scheduler, string jobName, string jobGroup, string cronExpression, CancellationToken cancellationToken)
        {
            var triggersFromConfiguration = new List<string>();
            foreach (var triggerWithData in this.options.Value.TriggersFromDb.Where(t => t.Key == jobName).Select(t => t.Value))
            {
                triggersFromConfiguration.AddRange(triggerWithData.Select(t => t.TriggerName));
            }

            var triggersForSchedule = await GetTriggersForSchedule(scheduler, jobName, triggersFromConfiguration, cancellationToken).ConfigureAwait(false);

            foreach (var triggerNameForSchedule in triggersForSchedule)
            {
                await this.ScheduleTriggerFromDb(scheduler, jobName, jobGroup, cronExpression, triggerNameForSchedule, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

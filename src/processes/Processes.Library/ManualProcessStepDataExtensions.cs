/********************************************************************************
 * Copyright (c) 2024 BMW Group AG
 * Copyright 2024 SAP SE or an SAP affiliate company and ssi-dim-middle-layer contributors.
 *
 * See the NOTICE file(s) distributed with this work for additional
 * information regarding copyright ownership.
 *
 * This program and the accompanying materials are made available under the
 * terms of the Apache License, Version 2.0 which is available at
 * https://www.apache.org/licenses/LICENSE-2.0.
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 * SPDX-License-Identifier: Apache-2.0
 ********************************************************************************/

using Dim.DbAccess;
using Dim.DbAccess.Repositories;
using Dim.Entities.Entities;
using Dim.Entities.Enums;
using Org.Eclipse.TractusX.Portal.Backend.Framework.DBAccess;
using Org.Eclipse.TractusX.Portal.Backend.Framework.ErrorHandling;

namespace Dim.Processes.Library;

public static class VerifyProcessDataExtensions
{
    public static ManualProcessStepData CreateManualProcessData(
        this VerifyProcessData? processData,
        ProcessStepTypeId processStepTypeId,
        IDimRepositories portalRepositories,
        Func<string> getProcessEntityName)
    {
        if (processData is null)
        {
            throw new NotFoundException($"{getProcessEntityName()} does not exist");
        }

        if (processData.Process == null)
        {
            throw new ConflictException($"{getProcessEntityName()} is not associated with any process");
        }

        if (processData.Process.IsLocked())
        {
            throw new ConflictException($"process {processData.Process.Id} associated with {getProcessEntityName()} is locked, lock expiry is set to {processData.Process.LockExpiryDate}");
        }

        if (processData.ProcessSteps == null)
        {
            throw new UnexpectedConditionException("processSteps should never be null here");
        }

        if (processData.ProcessSteps.Any(step => step.ProcessStepStatusId != ProcessStepStatusId.TODO))
        {
            throw new UnexpectedConditionException($"processSteps should never have any other status than TODO here");
        }

        if (processData.ProcessSteps.All(step => step.ProcessStepTypeId != processStepTypeId))
        {
            throw new ConflictException($"{getProcessEntityName()}, process step {processStepTypeId} is not eligible to run");
        }

        return new(processStepTypeId, processData.Process, processData.ProcessSteps, portalRepositories);
    }
}

public static class ManualProcessStepDataExtensions
{
    public static void RequestLock(this ManualProcessStepData context, DateTimeOffset lockExpiryDate)
    {
        context.PortalRepositories.Attach(context.Process);

        var isLocked = context.Process.TryLock(lockExpiryDate);
        if (!isLocked)
        {
            throw new UnexpectedConditionException("process TryLock should never fail here");
        }
    }

    public static void SkipProcessSteps(this ManualProcessStepData context, IEnumerable<ProcessStepTypeId> processStepTypeIds) =>
        context.PortalRepositories.GetInstance<IProcessStepRepository>()
            .AttachAndModifyProcessSteps(
                context.ProcessSteps
                    .Where(step => step.ProcessStepTypeId != context.ProcessStepTypeId)
                    .GroupBy(step => step.ProcessStepTypeId)
                    .IntersectBy(processStepTypeIds, group => group.Key)
                    .SelectMany(group => ModifyStepStatusRange(group, ProcessStepStatusId.SKIPPED)));

    public static void SkipProcessStepsExcept(this ManualProcessStepData context, IEnumerable<ProcessStepTypeId> processStepTypeIds) =>
        context.PortalRepositories.GetInstance<IProcessStepRepository>()
            .AttachAndModifyProcessSteps(
                context.ProcessSteps
                    .Where(step => step.ProcessStepTypeId != context.ProcessStepTypeId)
                    .GroupBy(step => step.ProcessStepTypeId)
                    .ExceptBy(processStepTypeIds, group => group.Key)
                    .SelectMany(group => ModifyStepStatusRange(group, ProcessStepStatusId.SKIPPED)));

    public static void ScheduleProcessSteps(this ManualProcessStepData context, IEnumerable<ProcessStepTypeId> processStepTypeIds) =>
        context.PortalRepositories.GetInstance<IProcessStepRepository>()
            .CreateProcessStepRange(
                processStepTypeIds
                    .Except(context.ProcessSteps.Select(step => step.ProcessStepTypeId))
                    .Select(stepTypeId => (stepTypeId, ProcessStepStatusId.TODO, context.Process.Id)));

    public static void FinalizeProcessStep(this ManualProcessStepData context)
    {
        context.PortalRepositories.GetInstance<IProcessStepRepository>().AttachAndModifyProcessSteps(
            ModifyStepStatusRange(context.ProcessSteps.Where(step => step.ProcessStepTypeId == context.ProcessStepTypeId), ProcessStepStatusId.DONE));

        context.PortalRepositories.Attach(context.Process);
        if (!context.Process.ReleaseLock())
        {
            context.Process.UpdateVersion();
        }
    }

    private static IEnumerable<(Guid, Action<ProcessStep>?, Action<ProcessStep>)> ModifyStepStatusRange(IEnumerable<ProcessStep> steps, ProcessStepStatusId processStepStatusId)
    {
        var firstStep = steps.FirstOrDefault();

        if (firstStep == null)
            yield break;

        foreach (var step in steps)
        {
            yield return (
                step.Id,
                null,
                ps => ps.ProcessStepStatusId = ps.Id == firstStep.Id
                    ? processStepStatusId
                    : ProcessStepStatusId.DUPLICATE);
        }
    }
}

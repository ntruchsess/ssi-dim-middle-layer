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

using Dim.Entities.Enums;

namespace Org.Eclipse.TractusX.Portal.Backend.Processes.Worker.Library;

public interface IProcessExecutor
{
    enum ProcessExecutionResult
    {
        SaveRequested = 1,
        LockRequested = 2,
        Unmodified = 3
    }
    IAsyncEnumerable<ProcessExecutionResult> ExecuteProcess(Guid processId, ProcessTypeId processTypeId, CancellationToken cancellationToken);
    IEnumerable<ProcessTypeId> GetRegisteredProcessTypeIds();
    IEnumerable<ProcessStepTypeId> GetExecutableStepTypeIds();
}

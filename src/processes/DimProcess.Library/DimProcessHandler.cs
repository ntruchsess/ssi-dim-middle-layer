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

using Dim.Clients.Api.Cf;
using Dim.Clients.Api.Dim;
using Dim.Clients.Api.Entitlements;
using Dim.Clients.Api.Provisioning;
using Dim.Clients.Api.Services;
using Dim.Clients.Api.SubAccounts;
using Dim.Clients.Api.Subscriptions;
using Dim.Clients.Token;
using Dim.DbAccess;
using Dim.DbAccess.Repositories;
using Dim.Entities.Enums;
using DimProcess.Library.Callback;
using DimProcess.Library.DependencyInjection;
using Microsoft.Extensions.Options;
using Org.Eclipse.TractusX.Portal.Backend.Framework.ErrorHandling;

namespace DimProcess.Library;

public class DimProcessHandler(
    IDimRepositories dimRepositories,
    ISubAccountClient subAccountClient,
    IServiceClient serviceClient,
    ISubscriptionClient subscriptionClient,
    IEntitlementClient entitlementClient,
    IProvisioningClient provisioningClient,
    ICfClient cfClient,
    IDimClient dimClient,
    ICallbackService callbackService,
    IOptions<DimHandlerSettings> options)
    : IDimProcessHandler
{
    private readonly DimHandlerSettings _settings = options.Value;

    public async Task<(IEnumerable<ProcessStepTypeId>? nextStepTypeIds, ProcessStepStatusId stepStatusId, bool modified, string? processMessage)> CreateSubaccount(Guid tenantId, string tenantName, CancellationToken cancellationToken)
    {
        var parentDirectoryId = _settings.RootDirectoryId;
        var adminMail = _settings.AdminMail;
        var subAccountAuth = new BasicAuthSettings
        {
            TokenAddress = $"{_settings.AuthUrl}/oauth/token",
            ClientId = _settings.ClientidCisCentral,
            ClientSecret = _settings.ClientsecretCisCentral
        };

        var subAccountId = await subAccountClient.CreateSubaccount(subAccountAuth, adminMail, tenantName, parentDirectoryId, cancellationToken).ConfigureAwait(false);
        dimRepositories.GetInstance<ITenantRepository>().AttachAndModifyTenant(tenantId, tenant =>
            {
                tenant.SubAccountId = null;
            },
            tenant =>
            {
                tenant.SubAccountId = subAccountId;
            });
        return new ValueTuple<IEnumerable<ProcessStepTypeId>?, ProcessStepStatusId, bool, string?>(
            Enumerable.Repeat(ProcessStepTypeId.CREATE_SERVICEMANAGER_BINDINGS, 1),
            ProcessStepStatusId.DONE,
            false,
            null);
    }

    public async Task<(IEnumerable<ProcessStepTypeId>? nextStepTypeIds, ProcessStepStatusId stepStatusId, bool modified, string? processMessage)> CreateServiceManagerBindings(Guid tenantId, CancellationToken cancellationToken)
    {
        var subAccountAuth = new BasicAuthSettings
        {
            TokenAddress = $"{_settings.AuthUrl}/oauth/token",
            ClientId = _settings.ClientidCisCentral,
            ClientSecret = _settings.ClientsecretCisCentral
        };

        var tenantRepository = dimRepositories.GetInstance<ITenantRepository>();
        var subAccountId = await tenantRepository.GetSubAccountIdByTenantId(tenantId).ConfigureAwait(false);
        if (subAccountId == null)
        {
            throw new ConflictException("SubAccountId must not be null.");
        }

        await subAccountClient.CreateServiceManagerBindings(subAccountAuth, subAccountId.Value, cancellationToken).ConfigureAwait(false);

        return new ValueTuple<IEnumerable<ProcessStepTypeId>?, ProcessStepStatusId, bool, string?>(
            Enumerable.Repeat(ProcessStepTypeId.ASSIGN_ENTITLEMENTS, 1),
            ProcessStepStatusId.DONE,
            false,
            null);
    }

    public async Task<(IEnumerable<ProcessStepTypeId>? nextStepTypeIds, ProcessStepStatusId stepStatusId, bool modified, string? processMessage)> AssignEntitlements(Guid tenantId, CancellationToken cancellationToken)
    {
        var subAccountAuth = new BasicAuthSettings
        {
            TokenAddress = $"{_settings.AuthUrl}/oauth/token",
            ClientId = _settings.ClientidCisCentral,
            ClientSecret = _settings.ClientsecretCisCentral
        };
        var subAccountId = await dimRepositories.GetInstance<ITenantRepository>().GetSubAccountIdByTenantId(tenantId).ConfigureAwait(false);
        if (subAccountId == null)
        {
            throw new ConflictException("SubAccountId must not be null.");
        }

        await entitlementClient.AssignEntitlements(subAccountAuth, subAccountId.Value, cancellationToken).ConfigureAwait(false);

        return new ValueTuple<IEnumerable<ProcessStepTypeId>?, ProcessStepStatusId, bool, string?>(
            Enumerable.Repeat(ProcessStepTypeId.CREATE_SERVICE_INSTANCE, 1),
            ProcessStepStatusId.DONE,
            false,
            null);
    }

    public async Task<(IEnumerable<ProcessStepTypeId>? nextStepTypeIds, ProcessStepStatusId stepStatusId, bool modified, string? processMessage)> CreateServiceInstance(Guid tenantId, CancellationToken cancellationToken)
    {
        var subAccountAuth = new BasicAuthSettings
        {
            TokenAddress = $"{_settings.AuthUrl}/oauth/token",
            ClientId = _settings.ClientidCisCentral,
            ClientSecret = _settings.ClientsecretCisCentral
        };
        var subAccountId = await dimRepositories.GetInstance<ITenantRepository>().GetSubAccountIdByTenantId(tenantId).ConfigureAwait(false);
        if (subAccountId == null)
        {
            throw new ConflictException("SubAccountId must not be null.");
        }

        var saBinding = await subAccountClient.GetServiceManagerBindings(subAccountAuth, subAccountId.Value, cancellationToken).ConfigureAwait(false);
        var serviceInstance = await serviceClient.CreateServiceInstance(saBinding, cancellationToken).ConfigureAwait(false);

        dimRepositories.GetInstance<ITenantRepository>().AttachAndModifyTenant(tenantId, tenant =>
            {
                tenant.ServiceInstanceId = null;
            },
            tenant =>
            {
                tenant.ServiceInstanceId = serviceInstance.Id;
            });
        return new ValueTuple<IEnumerable<ProcessStepTypeId>?, ProcessStepStatusId, bool, string?>(
            Enumerable.Repeat(ProcessStepTypeId.CREATE_SERVICE_BINDING, 1),
            ProcessStepStatusId.DONE,
            false,
            null);
    }

    public async Task<(IEnumerable<ProcessStepTypeId>? nextStepTypeIds, ProcessStepStatusId stepStatusId, bool modified, string? processMessage)> CreateServiceBindings(Guid tenantId, CancellationToken cancellationToken)
    {
        var subAccountAuth = new BasicAuthSettings
        {
            TokenAddress = $"{_settings.AuthUrl}/oauth/token",
            ClientId = _settings.ClientidCisCentral,
            ClientSecret = _settings.ClientsecretCisCentral
        };
        var (subAccountId, serviceInstanceId) = await dimRepositories.GetInstance<ITenantRepository>().GetSubAccountAndServiceInstanceIdsByTenantId(tenantId).ConfigureAwait(false);
        if (subAccountId == null)
        {
            throw new ConflictException("SubAccountId must not be null.");
        }

        if (string.IsNullOrEmpty(serviceInstanceId))
        {
            throw new ConflictException("ServiceInstanceId must not be null.");
        }

        var saBinding = await subAccountClient.GetServiceManagerBindings(subAccountAuth, subAccountId.Value, cancellationToken).ConfigureAwait(false);
        var serviceBinding = await serviceClient.CreateServiceBinding(saBinding, serviceInstanceId, cancellationToken).ConfigureAwait(false);

        dimRepositories.GetInstance<ITenantRepository>().AttachAndModifyTenant(tenantId, tenant =>
            {
                tenant.ServiceBindingName = null;
            },
            tenant =>
            {
                tenant.ServiceBindingName = serviceBinding.Name;
            });
        return new ValueTuple<IEnumerable<ProcessStepTypeId>?, ProcessStepStatusId, bool, string?>(
            Enumerable.Repeat(ProcessStepTypeId.SUBSCRIBE_APPLICATION, 1),
            ProcessStepStatusId.DONE,
            false,
            null);
    }

    public async Task<(IEnumerable<ProcessStepTypeId>? nextStepTypeIds, ProcessStepStatusId stepStatusId, bool modified, string? processMessage)> SubscribeApplication(Guid tenantId, CancellationToken cancellationToken)
    {
        var subAccountAuth = new BasicAuthSettings
        {
            TokenAddress = $"{_settings.AuthUrl}/oauth/token",
            ClientId = _settings.ClientidCisCentral,
            ClientSecret = _settings.ClientsecretCisCentral
        };
        var (subAccountId, serviceBindingName) = await dimRepositories.GetInstance<ITenantRepository>().GetSubAccountIdAndServiceBindingNameByTenantId(tenantId).ConfigureAwait(false);
        if (subAccountId == null)
        {
            throw new ConflictException("SubAccountId must not be null.");
        }

        if (string.IsNullOrEmpty(serviceBindingName))
        {
            throw new ConflictException("ServiceBindingName must not be null.");
        }

        var saBinding = await subAccountClient.GetServiceManagerBindings(subAccountAuth, subAccountId.Value, cancellationToken).ConfigureAwait(false);
        var bindingResponse = await serviceClient.GetServiceBinding(saBinding, serviceBindingName, cancellationToken).ConfigureAwait(false);
        await subscriptionClient.SubscribeApplication(saBinding.Url, bindingResponse, "decentralized-identity-management-app", "standard", cancellationToken).ConfigureAwait(false);

        return new ValueTuple<IEnumerable<ProcessStepTypeId>?, ProcessStepStatusId, bool, string?>(
            Enumerable.Repeat(ProcessStepTypeId.CREATE_CLOUD_FOUNDRY_ENVIRONMENT, 1),
            ProcessStepStatusId.DONE,
            false,
            null);
    }

    public async Task<(IEnumerable<ProcessStepTypeId>? nextStepTypeIds, ProcessStepStatusId stepStatusId, bool modified, string? processMessage)> CreateCloudFoundryEnvironment(Guid tenantId, string tenantName, CancellationToken cancellationToken)
    {
        var adminMail = _settings.AdminMail;
        var subAccountAuth = new BasicAuthSettings
        {
            TokenAddress = $"{_settings.AuthUrl}/oauth/token",
            ClientId = _settings.ClientidCisCentral,
            ClientSecret = _settings.ClientsecretCisCentral
        };
        var (subAccountId, serviceBindingName) = await dimRepositories.GetInstance<ITenantRepository>().GetSubAccountIdAndServiceBindingNameByTenantId(tenantId).ConfigureAwait(false);
        if (subAccountId == null)
        {
            throw new ConflictException("SubAccountId must not be null.");
        }

        if (string.IsNullOrEmpty(serviceBindingName))
        {
            throw new ConflictException("ServiceBindingName must not be null.");
        }

        var saBinding = await subAccountClient.GetServiceManagerBindings(subAccountAuth, subAccountId.Value, cancellationToken).ConfigureAwait(false);
        var bindingResponse = await serviceClient.GetServiceBinding(saBinding, serviceBindingName, cancellationToken).ConfigureAwait(false);
        await provisioningClient.CreateCloudFoundryEnvironment(saBinding.Url, bindingResponse, tenantName, adminMail, cancellationToken)
            .ConfigureAwait(false);

        return new ValueTuple<IEnumerable<ProcessStepTypeId>?, ProcessStepStatusId, bool, string?>(
            Enumerable.Repeat(ProcessStepTypeId.CREATE_CLOUD_FOUNDRY_SPACE, 1),
            ProcessStepStatusId.DONE,
            false,
            null);
    }

    public async Task<(IEnumerable<ProcessStepTypeId>? nextStepTypeIds, ProcessStepStatusId stepStatusId, bool modified, string? processMessage)> CreateCloudFoundrySpace(Guid tenantId, string tenantName, CancellationToken cancellationToken)
    {
        var spaceId = await cfClient.CreateCloudFoundrySpace(tenantName, cancellationToken).ConfigureAwait(false);

        dimRepositories.GetInstance<ITenantRepository>().AttachAndModifyTenant(tenantId, tenant =>
            {
                tenant.SpaceId = null;
            },
            tenant =>
            {
                tenant.SpaceId = spaceId;
            });
        return new ValueTuple<IEnumerable<ProcessStepTypeId>?, ProcessStepStatusId, bool, string?>(
            Enumerable.Repeat(ProcessStepTypeId.ADD_SPACE_MANAGER_ROLE, 1),
            ProcessStepStatusId.DONE,
            false,
            null);
    }

    public async Task<(IEnumerable<ProcessStepTypeId>? nextStepTypeIds, ProcessStepStatusId stepStatusId, bool modified, string? processMessage)> AddSpaceManagerRole(Guid tenantId, CancellationToken cancellationToken)
    {
        var adminMail = _settings.AdminMail;
        var spaceId = await dimRepositories.GetInstance<ITenantRepository>().GetSpaceId(tenantId).ConfigureAwait(false);
        if (spaceId == null)
        {
            throw new ConflictException("SpaceId must not be null.");
        }

        await cfClient.AddSpaceRoleToUser("space_manager", adminMail, spaceId.Value, cancellationToken).ConfigureAwait(false);

        return new ValueTuple<IEnumerable<ProcessStepTypeId>?, ProcessStepStatusId, bool, string?>(
            Enumerable.Repeat(ProcessStepTypeId.ADD_SPACE_DEVELOPER_ROLE, 1),
            ProcessStepStatusId.DONE,
            false,
            null);
    }

    public async Task<(IEnumerable<ProcessStepTypeId>? nextStepTypeIds, ProcessStepStatusId stepStatusId, bool modified, string? processMessage)> AddSpaceDeveloperRole(Guid tenantId, CancellationToken cancellationToken)
    {
        var adminMail = _settings.AdminMail;
        var spaceId = await dimRepositories.GetInstance<ITenantRepository>().GetSpaceId(tenantId).ConfigureAwait(false);
        if (spaceId == null)
        {
            throw new ConflictException("SpaceId must not be null.");
        }

        await cfClient.AddSpaceRoleToUser("space_developer", adminMail, spaceId.Value, cancellationToken).ConfigureAwait(false);

        return new ValueTuple<IEnumerable<ProcessStepTypeId>?, ProcessStepStatusId, bool, string?>(
            Enumerable.Repeat(ProcessStepTypeId.CREATE_DIM_SERVICE_INSTANCE, 1),
            ProcessStepStatusId.DONE,
            false,
            null);
    }

    public async Task<(IEnumerable<ProcessStepTypeId>? nextStepTypeIds, ProcessStepStatusId stepStatusId, bool modified, string? processMessage)> CreateDimServiceInstance(string tenantName, Guid tenantId, CancellationToken cancellationToken)
    {
        var servicePlanId = await cfClient.GetServicePlan("decentralized-identity-management", "standard", cancellationToken).ConfigureAwait(false);
        var spaceId = await cfClient.GetSpace(tenantName, cancellationToken).ConfigureAwait(false);
        await cfClient.CreateDimServiceInstance(tenantName, spaceId, servicePlanId, cancellationToken).ConfigureAwait(false);

        return new ValueTuple<IEnumerable<ProcessStepTypeId>?, ProcessStepStatusId, bool, string?>(
            Enumerable.Repeat(ProcessStepTypeId.CREATE_SERVICE_INSTANCE_BINDING, 1),
            ProcessStepStatusId.DONE,
            false,
            null);
    }

    public async Task<(IEnumerable<ProcessStepTypeId>? nextStepTypeIds, ProcessStepStatusId stepStatusId, bool modified, string? processMessage)> CreateServiceInstanceBindings(string tenantName, Guid tenantId, CancellationToken cancellationToken)
    {
        var spaceId = await dimRepositories.GetInstance<ITenantRepository>().GetSpaceId(tenantId).ConfigureAwait(false);
        if (spaceId == null)
        {
            throw new ConflictException("SpaceId must not be null.");
        }

        await cfClient.CreateServiceInstanceBindings(tenantName, null, spaceId.Value, cancellationToken).ConfigureAwait(false);

        return new ValueTuple<IEnumerable<ProcessStepTypeId>?, ProcessStepStatusId, bool, string?>(
            Enumerable.Repeat(ProcessStepTypeId.GET_DIM_DETAILS, 1),
            ProcessStepStatusId.DONE,
            false,
            null);
    }

    public async Task<(IEnumerable<ProcessStepTypeId>? nextStepTypeIds, ProcessStepStatusId stepStatusId, bool modified, string? processMessage)> GetDimDetails(string tenantName, Guid tenantId, CancellationToken cancellationToken)
    {
        var spaceId = await dimRepositories.GetInstance<ITenantRepository>().GetSpaceId(tenantId).ConfigureAwait(false);
        if (spaceId == null)
        {
            throw new ConflictException("SpaceId must not be null.");
        }

        var dimInstanceId = await cfClient.GetServiceBinding(tenantName, spaceId.Value, $"{tenantName}-dim-key01", cancellationToken).ConfigureAwait(false);

        dimRepositories.GetInstance<ITenantRepository>().AttachAndModifyTenant(tenantId, tenant =>
            {
                tenant.DimInstanceId = null;
            },
            tenant =>
            {
                tenant.DimInstanceId = dimInstanceId;
            });
        return new ValueTuple<IEnumerable<ProcessStepTypeId>?, ProcessStepStatusId, bool, string?>(
            Enumerable.Repeat(ProcessStepTypeId.CREATE_APPLICATION, 1),
            ProcessStepStatusId.DONE,
            false,
            null);
    }

    public async Task<(IEnumerable<ProcessStepTypeId>? nextStepTypeIds, ProcessStepStatusId stepStatusId, bool modified, string? processMessage)> CreateApplication(string tenantName, Guid tenantId, CancellationToken cancellationToken)
    {
        var (dimInstanceId, _, _) = await dimRepositories.GetInstance<ITenantRepository>().GetDimInstanceIdAndHostingUrl(tenantId).ConfigureAwait(false);
        if (dimInstanceId == null)
        {
            throw new ConflictException("DimInstanceId must not be null.");
        }

        var dimDetails = await cfClient.GetServiceBindingDetails(dimInstanceId.Value, cancellationToken).ConfigureAwait(false);

        var dimAuth = new BasicAuthSettings
        {
            TokenAddress = $"{dimDetails.Credentials.Uaa.Url}/oauth/token",
            ClientId = dimDetails.Credentials.Uaa.ClientId,
            ClientSecret = dimDetails.Credentials.Uaa.ClientSecret
        };
        var dimBaseUrl = dimDetails.Credentials.Url;
        var applicationId = await dimClient.CreateApplication(dimAuth, dimBaseUrl, tenantName, cancellationToken).ConfigureAwait(false);
        dimRepositories.GetInstance<ITenantRepository>().AttachAndModifyTenant(tenantId, tenant =>
            {
                tenant.ApplicationId = null;
            },
            tenant =>
            {
                tenant.ApplicationId = applicationId;
            });
        return new ValueTuple<IEnumerable<ProcessStepTypeId>?, ProcessStepStatusId, bool, string?>(
            Enumerable.Repeat(ProcessStepTypeId.CREATE_COMPANY_IDENTITY, 1),
            ProcessStepStatusId.DONE,
            false,
            null);
    }

    public async Task<(IEnumerable<ProcessStepTypeId>? nextStepTypeIds, ProcessStepStatusId stepStatusId, bool modified, string? processMessage)> CreateCompanyIdentity(Guid tenantId, CancellationToken cancellationToken)
    {
        var (dimInstanceId, hostingUrl, isIssuer) = await dimRepositories.GetInstance<ITenantRepository>().GetDimInstanceIdAndHostingUrl(tenantId).ConfigureAwait(false);
        if (dimInstanceId == null)
        {
            throw new ConflictException("DimInstanceId must not be null.");
        }

        var dimDetails = await cfClient.GetServiceBindingDetails(dimInstanceId.Value, cancellationToken).ConfigureAwait(false);

        var dimAuth = new BasicAuthSettings
        {
            TokenAddress = $"{dimDetails.Credentials.Uaa.Url}/oauth/token",
            ClientId = dimDetails.Credentials.Uaa.ClientId,
            ClientSecret = dimDetails.Credentials.Uaa.ClientSecret
        };
        var dimBaseUrl = dimDetails.Credentials.Url;
        var result = await dimClient.CreateCompanyIdentity(dimAuth, tenantId, hostingUrl, dimBaseUrl, isIssuer, cancellationToken).ConfigureAwait(false);

        dimRepositories.GetInstance<ITenantRepository>().AttachAndModifyTenant(tenantId, tenant =>
            {
                tenant.DidDownloadUrl = null;
                tenant.Did = null;
                tenant.CompanyId = null;
            },
            tenant =>
            {
                tenant.DidDownloadUrl = result.DownloadUrl;
                tenant.Did = result.Did;
                tenant.CompanyId = result.CompanyId;
            });
        return new ValueTuple<IEnumerable<ProcessStepTypeId>?, ProcessStepStatusId, bool, string?>(
            Enumerable.Repeat(ProcessStepTypeId.ASSIGN_COMPANY_APPLICATION, 1),
            ProcessStepStatusId.DONE,
            false,
            null);
    }

    public async Task<(IEnumerable<ProcessStepTypeId>? nextStepTypeIds, ProcessStepStatusId stepStatusId, bool modified, string? processMessage)> AssignCompanyApplication(Guid tenantId, CancellationToken cancellationToken)
    {
        var (applicationId, companyId, dimInstanceId, isIssuer) = await dimRepositories.GetInstance<ITenantRepository>().GetApplicationAndCompanyId(tenantId).ConfigureAwait(false);
        if (applicationId == null)
        {
            throw new ConflictException("ApplicationId must always be set here");
        }

        if (companyId == null)
        {
            throw new ConflictException("CompanyId must always be set here");
        }

        if (dimInstanceId == null)
        {
            throw new ConflictException("DimInstanceId must not be null.");
        }

        var dimDetails = await cfClient.GetServiceBindingDetails(dimInstanceId.Value, cancellationToken).ConfigureAwait(false);
        var dimAuth = new BasicAuthSettings
        {
            TokenAddress = $"{dimDetails.Credentials.Uaa.Url}/oauth/token",
            ClientId = dimDetails.Credentials.Uaa.ClientId,
            ClientSecret = dimDetails.Credentials.Uaa.ClientSecret
        };
        var dimBaseUrl = dimDetails.Credentials.Url;
        var applicationKey = await dimClient.GetApplication(dimAuth, dimBaseUrl, applicationId, cancellationToken);
        await dimClient.AssignApplicationToCompany(dimAuth, dimBaseUrl, applicationKey, companyId.Value, cancellationToken).ConfigureAwait(false);

        dimRepositories.GetInstance<ITenantRepository>().AttachAndModifyTenant(tenantId, tenant =>
            {
                tenant.ApplicationKey = null;
            },
            tenant =>
            {
                tenant.ApplicationKey = applicationKey;
            });
        return new ValueTuple<IEnumerable<ProcessStepTypeId>?, ProcessStepStatusId, bool, string?>(
            Enumerable.Repeat(isIssuer ? ProcessStepTypeId.CREATE_STATUS_LIST : ProcessStepTypeId.SEND_CALLBACK, 1),
            ProcessStepStatusId.DONE,
            false,
            null);
    }

    public async Task<(IEnumerable<ProcessStepTypeId>? nextStepTypeIds, ProcessStepStatusId stepStatusId, bool modified, string? processMessage)> CreateStatusList(Guid tenantId, CancellationToken cancellationToken)
    {
        var (_, companyId, dimInstanceId, _) = await dimRepositories.GetInstance<ITenantRepository>().GetApplicationAndCompanyId(tenantId).ConfigureAwait(false);
        if (companyId == null)
        {
            throw new ConflictException("CompanyId must always be set here");
        }

        if (dimInstanceId == null)
        {
            throw new ConflictException("DimInstanceId must not be null.");
        }

        var dimDetails = await cfClient.GetServiceBindingDetails(dimInstanceId.Value, cancellationToken).ConfigureAwait(false);
        var dimAuth = new BasicAuthSettings
        {
            TokenAddress = $"{dimDetails.Credentials.Uaa.Url}/oauth/token",
            ClientId = dimDetails.Credentials.Uaa.ClientId,
            ClientSecret = dimDetails.Credentials.Uaa.ClientSecret
        };
        var dimBaseUrl = dimDetails.Credentials.Url;
        await dimClient.CreateStatusList(dimAuth, dimBaseUrl, companyId.Value, cancellationToken).ConfigureAwait(false);

        return new ValueTuple<IEnumerable<ProcessStepTypeId>?, ProcessStepStatusId, bool, string?>(
            Enumerable.Repeat(ProcessStepTypeId.SEND_CALLBACK, 1),
            ProcessStepStatusId.DONE,
            false,
            null);
    }

    public async Task<(IEnumerable<ProcessStepTypeId>? nextStepTypeIds, ProcessStepStatusId stepStatusId, bool modified, string? processMessage)> SendCallback(Guid tenantId, CancellationToken cancellationToken)
    {
        var (bpn, downloadUrl, did, dimInstanceId) = await dimRepositories.GetInstance<ITenantRepository>().GetCallbackData(tenantId).ConfigureAwait(false);
        if (downloadUrl == null)
        {
            throw new ConflictException("DownloadUrl must not be null.");
        }

        if (did == null)
        {
            throw new ConflictException("Did must not be null.");
        }

        if (dimInstanceId == null)
        {
            throw new ConflictException("DimInstanceId must not be null.");
        }

        var dimDetails = await cfClient.GetServiceBindingDetails(dimInstanceId.Value, cancellationToken).ConfigureAwait(false);
        var didDocument = await dimClient.GetDidDocument(downloadUrl, cancellationToken).ConfigureAwait(false);

        await callbackService.SendCallback(bpn, dimDetails, didDocument, did, cancellationToken).ConfigureAwait(false);

        return new ValueTuple<IEnumerable<ProcessStepTypeId>?, ProcessStepStatusId, bool, string?>(
            null,
            ProcessStepStatusId.DONE,
            false,
            null);
    }
}

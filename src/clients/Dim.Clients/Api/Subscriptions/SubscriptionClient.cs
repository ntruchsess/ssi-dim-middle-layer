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

using Dim.Clients.Api.Services;
using Dim.Clients.Extensions;
using Dim.Clients.Token;
using Org.Eclipse.TractusX.Portal.Backend.Framework.HttpClientExtensions;
using System.Net.Http.Json;

namespace Dim.Clients.Api.Subscriptions;

public class SubscriptionClient : ISubscriptionClient
{
    private readonly IBasicAuthTokenService _basicAuthTokenService;

    public SubscriptionClient(IBasicAuthTokenService basicAuthTokenService)
    {
        _basicAuthTokenService = basicAuthTokenService;
    }

    public async Task SubscribeApplication(string authUrl, BindingItem bindingData, string applicationName, string planName, CancellationToken cancellationToken)
    {
        var authSettings = new BasicAuthSettings
        {
            TokenAddress = $"{authUrl}/oauth/token",
            ClientId = bindingData.Credentials.Uaa.ClientId,
            ClientSecret = bindingData.Credentials.Uaa.ClientSecret
        };
        var client = await _basicAuthTokenService.GetBasicAuthorizedClient<SubscriptionClient>(authSettings, cancellationToken).ConfigureAwait(false);
        var data = new
        {
            planName = planName
        };

        await client.PostAsJsonAsync($"{bindingData.Credentials.Endpoints.SaasRegistryServiceUrl}/saas-manager/v1/applications/{applicationName}/subscription", data, JsonSerializerExtensions.Options, cancellationToken)
            .CatchingIntoServiceExceptionFor("subscribe-application", HttpAsyncResponseMessageExtension.RecoverOptions.INFRASTRUCTURE).ConfigureAwait(false);
    }
}

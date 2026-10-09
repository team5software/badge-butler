// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

using Microsoft.AspNetCore.DataProtection.Repositories;

namespace T5S.BadgeButler.Api.Authentication;

/// <summary>
/// Keeps DataProtection keys in memory. AddAuthentication registers DataProtection, but nothing in
/// this app protects data with it, so its keys don't need to survive a restart.
/// </summary>
public sealed class InMemoryXmlRepository : IXmlRepository
{
    private readonly ConcurrentQueue<XElement> _elements = new();

    public IReadOnlyCollection<XElement> GetAllElements() => _elements.Select(element => new XElement(element)).ToList();

    public void StoreElement(XElement element, string friendlyName) => _elements.Enqueue(new XElement(element));
}

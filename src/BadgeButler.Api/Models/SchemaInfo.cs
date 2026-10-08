// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

namespace T5S.BadgeButler.Api.Models;

public sealed record SchemaInfo(
    int SchemaVersion,
    string ConstantsFingerprint);

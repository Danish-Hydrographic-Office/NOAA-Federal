using ArcGIS.Core.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using Version = ArcGIS.Core.Data.Version;

namespace Geodatastyrelsen.ArcGIS.Modules
{
    internal static class LittleHelper
    {
        public static void DetectChanges(
                    IEnumerable<FeatureClassDefinition> definitions,
                    IEnumerable<DifferenceType> differenceTypesEdits,
                    Version currentVersion,
                    Version root,
                    Action<string, long, Feature>? callback) {
            using var currentGeodatabase = currentVersion.Connect();
            using var defaultGeodatabase = root.Connect();

            foreach (var definition in definitions) {
                switch (definition.GetName().Split('.').Last().ToUpperInvariant()) {
                    case "PRODUCTCOVERAGE":
                        continue;
                }

                using var source = currentGeodatabase.OpenDataset<FeatureClass>(definition.GetName());

                if (source.GetRegistrationType().Equals(RegistrationType.Nonversioned))
                    continue;

                using var target = defaultGeodatabase.OpenDataset<FeatureClass>(definition.GetName());

                var filter = new QueryFilter {
                    WhereClause = "1=1",
                    //SubFields = All since sub calls may use different fields!
                };

                //var count = 0;
                var countUpdate = 0;
                var countInsert = 0;
                var countDelete = 0;
                var countDefault = 0;

                var oids = new List<long>();

                //  Updated
                foreach (var differenceType in differenceTypesEdits) {
                    ref int count = ref countDefault;

                    switch (differenceType) {
                        case DifferenceType.Insert:
                            count = ref countInsert; break;

                        case DifferenceType.UpdateUpdate:
                        case DifferenceType.UpdateNoChange:
                        case DifferenceType.UpdateDelete:
                            count = ref countUpdate; break;

                        case DifferenceType.DeleteNoChange:
                        case DifferenceType.DeleteUpdate:
                            count = ref countDelete; break;

                        default:
                            if (System.Diagnostics.Debugger.IsAttached)
                                System.Diagnostics.Debugger.Break();
                            break;
                    }

                    using var cursor = source.Differences(target, differenceType, filter);
                    while (cursor.MoveNext()) {
                        count += 1;

                        var current = cursor.Current;
                        if (current == default) {
                            oids.Add(cursor.ObjectID);
                            continue;
                        }
                        else {
                            callback?.Invoke(definition.GetName(), Convert.ToInt64(current["PLTS_COMP_SCALE"]), (Feature)current);
                        }
                    }
                }

                //  Deleted
                foreach (var chunk in oids.Chunk(512)) {
                    using var cursor = target.Search(new QueryFilter {
                        WhereClause = $"{definition.GetObjectIDField()} in ({string.Join(",", chunk)})",
                    }, true);

                    while (cursor.MoveNext()) {
                        var current = cursor.Current;
                        callback?.Invoke(definition.GetName(), Convert.ToInt64(current["PLTS_COMP_SCALE"]), (Feature)current);
                    }
                }

                if (countInsert > 0)
                    Logger.Current.Verbose("dataset: {dataset} INSERT #{count}", definition.GetName(), countInsert);
                if (countUpdate > 0)
                    Logger.Current.Verbose("dataset: {dataset} UPDATE #{count}", definition.GetName(), countUpdate);
                if (countDelete > 0)
                    Logger.Current.Verbose("dataset: {dataset} DELETE #{count}", definition.GetName(), countDelete);
                if (countDefault > 0)
                    Logger.Current.Verbose("dataset: {dataset} DEFAULT #{count}", definition.GetName(), countDefault);
            }
        }
    }
}

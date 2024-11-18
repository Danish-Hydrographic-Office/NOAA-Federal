using ArcGIS.Core.Data;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Geodatastyrelsen.ArcGIS.Modules
{
    internal class ButtonSelectUnverified : Button
    {
        private Module1 _module { get; set; } = default;

        public ButtonSelectUnverified() {
            _module = Module1.Current;
        }

        protected override async void OnClick() {
            Func<Action<IReadOnlyList<MapMember>>> selector = default;

            if (MapView.Active?.Map == default)
                return;

            var layers = MapView.Active.Map.GetLayersAsFlattenedList().OfType<FeatureLayer>();

            var layer = await QueuedTask.Run(() => {
                if (layers.FirstOrDefault(e => {
                    if (!e.IsEditable)
                        return false;
                    if (!e.GetFieldDescriptions().Any(f => f.Name.Equals("PLTS_COMP_SCALE")))
                        return false;
                    return true;
                }) is FeatureLayer layer) {
                    return layer;
                }
                return default;
            });

            if (layer == default) {
                Logger.Current.Verbose("Non nautical database...");
                return;
            }

            Logger.Current.Debug("Selecting unverified records [GST]");

            using (var progress = new ProgressDialog("Selecting unverified records...")) {
                var status = new CancelableProgressorSource(progress);
                progress.Show();

                try {
                    await QueuedTask.Run(() => {
                        var datastore = layer.GetFeatureClass().GetDatastore();

                        if (datastore is Geodatabase geodatabase) {
                            var currentVersion = geodatabase.GetVersionManager().GetCurrentVersion();
                            var defaultVersion = geodatabase.GetVersionManager().GetDefaultVersion();

                            if (currentVersion.GetName().Equals(defaultVersion.GetName())) {
                                Logger.Current.Debug("selector: {versionName}", currentVersion.GetName());

                                selector = () => {
                                    using var currentGeodatabase = currentVersion.Connect();

                                    var definitions = currentGeodatabase.GetDefinitions<FeatureClassDefinition>();

                                    var dictionary = new Dictionary<string, Selection>();

                                    foreach (var definition in definitions) {
                                        switch (definition.GetName().Split('.').Last().ToUpperInvariant()) {
                                            case "PRODUCTCOVERAGE":
                                                continue;
                                        }

                                        using var source = currentGeodatabase.OpenDataset<FeatureClass>(definition.GetName());

                                        var filter = new QueryFilter {
                                            WhereClause = "NIS_VERIFIED IS NULL OR NIS_VERIFIED != 1",
                                        };

                                        var selection = source.Select(filter, SelectionType.ObjectID, SelectionOption.Normal);
                                        if (selection.GetCount() > 0)
                                            dictionary.Add(definition.GetName(), selection);
                                    };

                                    return new Action<IReadOnlyList<MapMember>>((members) => {
                                        if (dictionary.Count == 0) {
                                            Logger.Current.Information("selector: Empty result");
                                            return;
                                        }
                                        foreach (var member in members.Where(e => e is FeatureLayer)) {
                                            var featureLayer = (FeatureLayer)member;
                                            var name = featureLayer.GetFeatureClass().GetName();

                                            if (!dictionary.ContainsKey(name))
                                                continue;

                                            featureLayer.SetSelection(dictionary[name]);
                                        }
                                    });
                                };
                            }
                            else {
                                Logger.Current.Debug("selector: currentVersion {currentVersion}, defaultVersion {defaultVersion}", currentVersion.GetName(), defaultVersion.GetName());

                                selector = () => {
                                    var differenceTypesEdits = new DifferenceType[] {
                                    DifferenceType.Insert,
                                    DifferenceType.UpdateNoChange,
                                    DifferenceType.UpdateUpdate,
                                    DifferenceType.UpdateDelete,
                                    //DifferenceType.DeleteUpdate,      //  PLTS_DELETES
                                    //DifferenceType.DeleteNoChange,    //  PLTS_DELETES
                                };

                                    using var defaultGeodatabase = defaultVersion.Connect();

                                    using var currentGeodatabase = currentVersion.Connect();

                                    using var nautical = geodatabase.OpenDataset<FeatureDataset>("NIS.Nautical");

                                    var spatialReference = nautical.GetDefinition().GetSpatialReference();

                                    var definitions = currentGeodatabase.GetDefinitions<FeatureClassDefinition>();

                                    var dictionary = new Dictionary<string, List<long>>();

                                    LittleHelper.DetectChanges(
                                        definitions,
                                        differenceTypesEdits,
                                        currentVersion,
                                        defaultVersion,
                                        (tableName, plts_comp_scale, current) => {
                                            var nis_verified = DBNull.Value == current["NIS_VERIFIED"] ? 0 : Convert.ToInt16(current["NIS_VERIFIED"]);
                                            if (nis_verified == 1)
                                                return;
                                            if (!dictionary.ContainsKey(tableName))
                                                dictionary.Add(tableName, new List<long>());
                                            dictionary[tableName].Add(current.GetObjectID());
                                        }
                                    );

                                    return new Action<IReadOnlyList<MapMember>>((members) => {
                                        if (dictionary.Count == 0) {
                                            Logger.Current.Information("selector: Empty result");
                                            return;
                                        }
                                        foreach (var member in members.Where(e => e is FeatureLayer)) {
                                            var featureLayer = (FeatureLayer)member;
                                            var name = featureLayer.GetFeatureClass().GetName();

                                            if (!dictionary.ContainsKey(name))
                                                continue;

                                            var selectionCombinationMethod = SelectionCombinationMethod.New;

                                            foreach (var chunk in dictionary[name].Chunk(1000)) {
                                                featureLayer.Select(new QueryFilter {
                                                    WhereClause = $"OBJECTID IN ({string.Join(",", chunk)})",
                                                }, selectionCombinationMethod);
                                                selectionCombinationMethod = SelectionCombinationMethod.Add;
                                            }
                                        }
                                    });
                                };
                            }
                        }
                        else {
                            Logger.Current.Warning("Unknown datastore type ({datastore})", datastore.GetType().Name);
                            return;
                        }
                    }, status.Progressor, System.Threading.Tasks.TaskCreationOptions.None);

                    await QueuedTask.Run(() => {
                        var map = MapView.Active;

                        var members = map.Map.GetMapMembersAsFlattenedList();
                        selector?.Invoke()?.Invoke(members);
                    }, status.Progressor, System.Threading.Tasks.TaskCreationOptions.LongRunning);
                }
                catch (System.Exception ex) {
                    Logger.Current.Fatal(ex, "ButtonSelectUnverified::OnClick");
                    MessageBox.Show("Select unverified failed to execute!", "Oops!", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}

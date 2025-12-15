using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using PlantCad.Gui.Services;

namespace PlantCad.Gui.ViewModels.Tools;

public partial class PlantDbToolViewModel : Tool
{
    public ObservableCollection<PlantTypeRow> PlantTypes { get; } = new();
    public ObservableCollection<HabitRow> Habits { get; } = new();
    public ObservableCollection<ExposureRow> Exposures { get; } = new();
    public ObservableCollection<MoistureLevelRow> MoistureLevels { get; } = new();
    public ObservableCollection<PhClassRow> PhClasses { get; } = new();
    public ObservableCollection<SoilTraitRow> SoilTraits { get; } = new();
    public ObservableCollection<ColorRow> Colors { get; } = new();
    public ObservableCollection<FeatureRow> Features { get; } = new();
    public ObservableCollection<FoliagePersistenceRow> FoliagePersistences { get; } = new();
    public ObservableCollection<PackagingRow> Packagings { get; } = new();

    public string? DatabasePath => ServiceRegistry.PlantDbService?.DatabasePath;

    public IAsyncRelayCommand ReloadLookupsAsyncCommand { get; }
    public IAsyncRelayCommand SaveLookupsAsyncCommand { get; }
    public IAsyncRelayCommand<object?> DeleteSelectedAsyncCommand { get; }
    public IRelayCommand AddPlantTypeCommand { get; }
    public IRelayCommand AddHabitCommand { get; }
    public IRelayCommand AddExposureCommand { get; }
    public IRelayCommand AddMoistureLevelCommand { get; }
    public IRelayCommand AddPhClassCommand { get; }
    public IRelayCommand AddSoilTraitCommand { get; }
    public IRelayCommand AddColorCommand { get; }
    public IRelayCommand AddFeatureCommand { get; }
    public IRelayCommand AddFoliagePersistenceCommand { get; }
    public IRelayCommand AddPackagingCommand { get; }
    public IRelayCommand OpenPlantsDocumentCommand { get; }

    public PlantDbToolViewModel()
    {
        Title = "Plant Database";
        CanClose = false;
        DockGroup = "Tools";
        ReloadLookupsAsyncCommand = new AsyncRelayCommand(ReloadLookupsAsync);
        SaveLookupsAsyncCommand = new AsyncRelayCommand(SaveLookupsAsync);
        DeleteSelectedAsyncCommand = new AsyncRelayCommand<object?>(DeleteSelectedAsync);
        AddPlantTypeCommand = new RelayCommand(AddPlantType);
        AddHabitCommand = new RelayCommand(AddHabit);
        AddExposureCommand = new RelayCommand(AddExposure);
        AddMoistureLevelCommand = new RelayCommand(AddMoistureLevel);
        AddPhClassCommand = new RelayCommand(AddPhClass);
        AddSoilTraitCommand = new RelayCommand(AddSoilTrait);
        AddColorCommand = new RelayCommand(AddColor);
        AddFeatureCommand = new RelayCommand(AddFeature);
        AddFoliagePersistenceCommand = new RelayCommand(AddFoliagePersistence);
        AddPackagingCommand = new RelayCommand(AddPackaging);
        OpenPlantsDocumentCommand = new RelayCommand(OpenPlantsDocument);
        _ = ReloadLookupsAsync();
    }

    private async Task ReloadLookupsAsync()
    {
        var svc = ServiceRegistry.PlantDbService;
        if (svc is null || !svc.IsOpen)
            return;
        PlantTypes.Clear();
        Habits.Clear();
        Exposures.Clear();
        MoistureLevels.Clear();
        PhClasses.Clear();
        SoilTraits.Clear();
        Colors.Clear();
        Features.Clear();
        FoliagePersistences.Clear();
        Packagings.Clear();
        var types = await svc.GetPlantTypesAsync();
        foreach (var t in types)
            PlantTypes.Add(
                new PlantTypeRow
                {
                    Id = t.Id,
                    Code = t.Code,
                    NamePl = t.NamePl,
                }
            );
        var habits = await svc.GetHabitsAsync();
        foreach (var h in habits)
            Habits.Add(
                new HabitRow
                {
                    Id = h.Id,
                    Code = h.Code,
                    NamePl = h.NamePl,
                }
            );
        var exposures = await svc.GetExposuresAsync();
        foreach (var e in exposures)
            Exposures.Add(
                new ExposureRow
                {
                    Id = e.Id,
                    Code = e.Code,
                    NamePl = e.NamePl,
                }
            );
        var moist = await svc.GetMoistureLevelsAsync();
        foreach (var m in moist)
            MoistureLevels.Add(
                new MoistureLevelRow
                {
                    Id = m.Id,
                    Ordinal = m.Ordinal,
                    Code = m.Code,
                    NamePl = m.NamePl,
                }
            );
        var phs = await svc.GetPhClassesAsync();
        foreach (var p in phs)
            PhClasses.Add(
                new PhClassRow
                {
                    Id = p.Id,
                    Code = p.Code,
                    NamePl = p.NamePl,
                    MinPh = p.MinPh,
                    MaxPh = p.MaxPh,
                }
            );
        var soils = await svc.GetSoilTraitsAsync();
        foreach (var s in soils)
            SoilTraits.Add(
                new SoilTraitRow
                {
                    Id = s.Id,
                    Code = s.Code,
                    NamePl = s.NamePl,
                    Category = s.Category,
                }
            );
        var colors = await svc.GetColorsAsync();
        foreach (var c in colors)
            Colors.Add(
                new ColorRow
                {
                    Id = c.Id,
                    CanonicalEn = c.CanonicalEn,
                    NamePl = c.NamePl,
                    Hex = c.Hex,
                }
            );
        var features = await svc.GetFeaturesAsync();
        foreach (var f in features)
            Features.Add(
                new FeatureRow
                {
                    Id = f.Id,
                    Code = f.Code,
                    NamePl = f.NamePl,
                    GroupCode = f.GroupCode,
                }
            );
        var fols = await svc.GetFoliagePersistencesAsync();
        foreach (var fp in fols)
            FoliagePersistences.Add(
                new FoliagePersistenceRow
                {
                    Id = fp.Id,
                    Code = fp.Code,
                    NamePl = fp.NamePl,
                }
            );
        var packs = await svc.GetPackagingsAsync();
        foreach (var pk in packs)
            Packagings.Add(
                new PackagingRow
                {
                    Id = pk.Id,
                    Code = pk.Code,
                    NamePl = pk.NamePl,
                }
            );
    }

    private async Task SaveLookupsAsync()
    {
        var svc = ServiceRegistry.PlantDbService;
        if (svc is null || !svc.IsOpen)
            return;
        foreach (var t in PlantTypes)
        {
            var id = await svc.UpsertPlantTypeAsync(
                new PlantTypeDto
                {
                    Id = t.Id,
                    Code = t.Code ?? string.Empty,
                    NamePl = t.NamePl ?? string.Empty,
                }
            );
            t.Id = id;
        }
        foreach (var h in Habits)
        {
            var id = await svc.UpsertHabitAsync(
                new HabitDto
                {
                    Id = h.Id,
                    Code = h.Code ?? string.Empty,
                    NamePl = h.NamePl ?? string.Empty,
                }
            );
            h.Id = id;
        }
        foreach (var e in Exposures)
        {
            var id = await svc.UpsertExposureAsync(
                new ExposureDto
                {
                    Id = e.Id,
                    Code = e.Code ?? string.Empty,
                    NamePl = e.NamePl ?? string.Empty,
                }
            );
            e.Id = id;
        }
        foreach (var m in MoistureLevels)
        {
            var id = await svc.UpsertMoistureLevelAsync(
                new MoistureLevelDto
                {
                    Id = m.Id,
                    Ordinal = m.Ordinal,
                    Code = m.Code ?? string.Empty,
                    NamePl = m.NamePl ?? string.Empty,
                }
            );
            m.Id = id;
        }
        foreach (var p in PhClasses)
        {
            var id = await svc.UpsertPhClassAsync(
                new PhClassDto
                {
                    Id = p.Id,
                    Code = p.Code ?? string.Empty,
                    NamePl = p.NamePl ?? string.Empty,
                    MinPh = p.MinPh,
                    MaxPh = p.MaxPh,
                }
            );
            p.Id = id;
        }
        foreach (var s in SoilTraits)
        {
            var id = await svc.UpsertSoilTraitAsync(
                new SoilTraitDto
                {
                    Id = s.Id,
                    Code = s.Code ?? string.Empty,
                    NamePl = s.NamePl ?? string.Empty,
                    Category = s.Category ?? string.Empty,
                }
            );
            s.Id = id;
        }
        foreach (var c in Colors)
        {
            var id = await svc.UpsertColorAsync(
                new ColorDto
                {
                    Id = c.Id,
                    CanonicalEn = c.CanonicalEn ?? string.Empty,
                    NamePl = c.NamePl ?? string.Empty,
                    Hex = c.Hex,
                }
            );
            c.Id = id;
        }
        foreach (var f in Features)
        {
            var id = await svc.UpsertFeatureAsync(
                new FeatureDto
                {
                    Id = f.Id,
                    Code = f.Code ?? string.Empty,
                    NamePl = f.NamePl ?? string.Empty,
                    GroupCode = f.GroupCode ?? string.Empty,
                }
            );
            f.Id = id;
        }
        foreach (var fp in FoliagePersistences)
        {
            var id = await svc.UpsertFoliagePersistenceAsync(
                new FoliagePersistenceDto
                {
                    Id = fp.Id,
                    Code = fp.Code ?? string.Empty,
                    NamePl = fp.NamePl ?? string.Empty,
                }
            );
            fp.Id = id;
        }
        foreach (var pk in Packagings)
        {
            var id = await svc.UpsertPackagingAsync(
                new PackagingDto
                {
                    Id = pk.Id,
                    Code = pk.Code ?? string.Empty,
                    NamePl = pk.NamePl ?? string.Empty,
                }
            );
            pk.Id = id;
        }
        await ReloadLookupsAsync();
    }

    private async Task DeleteSelectedAsync(object? row)
    {
        var svc = ServiceRegistry.PlantDbService;
        if (svc is null || !svc.IsOpen || row is null)
            return;
        switch (row)
        {
            case PlantTypeRow t when t.Id > 0:
                await svc.DeletePlantTypeAsync(t.Id);
                PlantTypes.Remove(t);
                break;
            case HabitRow h when h.Id > 0:
                await svc.DeleteHabitAsync(h.Id);
                Habits.Remove(h);
                break;
            case ExposureRow e when e.Id > 0:
                await svc.DeleteExposureAsync(e.Id);
                Exposures.Remove(e);
                break;
            case MoistureLevelRow m when m.Id > 0:
                await svc.DeleteMoistureLevelAsync(m.Id);
                MoistureLevels.Remove(m);
                break;
            case PhClassRow p when p.Id > 0:
                await svc.DeletePhClassAsync(p.Id);
                PhClasses.Remove(p);
                break;
            case SoilTraitRow s when s.Id > 0:
                await svc.DeleteSoilTraitAsync(s.Id);
                SoilTraits.Remove(s);
                break;
            case ColorRow c when c.Id > 0:
                await svc.DeleteColorAsync(c.Id);
                Colors.Remove(c);
                break;
            case FeatureRow f when f.Id > 0:
                await svc.DeleteFeatureAsync(f.Id);
                Features.Remove(f);
                break;
            case FoliagePersistenceRow fp when fp.Id > 0:
                await svc.DeleteFoliagePersistenceAsync(fp.Id);
                FoliagePersistences.Remove(fp);
                break;
            case PackagingRow pk when pk.Id > 0:
                await svc.DeletePackagingAsync(pk.Id);
                Packagings.Remove(pk);
                break;
        }
    }

    private void AddPlantType()
    {
        PlantTypes.Add(new PlantTypeRow { Code = "new_type", NamePl = "Nowy typ" });
    }

    private void AddHabit()
    {
        Habits.Add(new HabitRow { Code = "new_habit", NamePl = "Nowy pokrój" });
    }

    private void AddExposure() =>
        Exposures.Add(new ExposureRow { Code = "new_exposure", NamePl = "Nowa ekspozycja" });

    private void AddMoistureLevel() =>
        MoistureLevels.Add(
            new MoistureLevelRow
            {
                Ordinal = MoistureLevels.Count + 1,
                Code = "new_moist",
                NamePl = "Poziom",
            }
        );

    private void AddPhClass() =>
        PhClasses.Add(
            new PhClassRow
            {
                Code = "new_ph",
                NamePl = "Klasa pH",
                MinPh = 6.5,
                MaxPh = 7.5,
            }
        );

    private void AddSoilTrait() =>
        SoilTraits.Add(
            new SoilTraitRow
            {
                Code = "new_soil",
                NamePl = "Cecha gleby",
                Category = "general",
            }
        );

    private void AddColor() =>
        Colors.Add(
            new ColorRow
            {
                CanonicalEn = "white",
                NamePl = "Biały",
                Hex = "#FFFFFF",
            }
        );

    private void AddFeature() =>
        Features.Add(
            new FeatureRow
            {
                Code = "new_feature",
                NamePl = "Cechy",
                GroupCode = "general",
            }
        );

    private void AddFoliagePersistence() =>
        FoliagePersistences.Add(
            new FoliagePersistenceRow { Code = "evergreen", NamePl = "Zimozielone" }
        );

    private void AddPackaging() =>
        Packagings.Add(new PackagingRow { Code = "new_pack", NamePl = "Opakowanie" });

    private void OpenPlantsDocument()
    {
        ServiceRegistry.RequestOpenPlantsDocument();
    }

    public sealed class PlantTypeRow
    {
        public int Id { get; set; }
        public string? Code { get; set; }
        public string? NamePl { get; set; }
    }

    public sealed class HabitRow
    {
        public int Id { get; set; }
        public string? Code { get; set; }
        public string? NamePl { get; set; }
    }

    public sealed class ExposureRow
    {
        public int Id { get; set; }
        public string? Code { get; set; }
        public string? NamePl { get; set; }
    }

    public sealed class MoistureLevelRow
    {
        public int Id { get; set; }
        public int Ordinal { get; set; }
        public string? Code { get; set; }
        public string? NamePl { get; set; }
    }

    public sealed class PhClassRow
    {
        public int Id { get; set; }
        public string? Code { get; set; }
        public string? NamePl { get; set; }
        public double MinPh { get; set; }
        public double MaxPh { get; set; }
    }

    public sealed class SoilTraitRow
    {
        public int Id { get; set; }
        public string? Code { get; set; }
        public string? NamePl { get; set; }
        public string? Category { get; set; }
    }

    public sealed class ColorRow
    {
        public int Id { get; set; }
        public string? CanonicalEn { get; set; }
        public string? NamePl { get; set; }
        public string? Hex { get; set; }
    }

    public sealed class FeatureRow
    {
        public int Id { get; set; }
        public string? Code { get; set; }
        public string? NamePl { get; set; }
        public string? GroupCode { get; set; }
    }

    public sealed class FoliagePersistenceRow
    {
        public int Id { get; set; }
        public string? Code { get; set; }
        public string? NamePl { get; set; }
    }

    public sealed class PackagingRow
    {
        public int Id { get; set; }
        public string? Code { get; set; }
        public string? NamePl { get; set; }
    }
}

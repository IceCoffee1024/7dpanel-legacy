using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.DependencyInjection;
using LSTY.SevenDPanel.Application.GeoIp;
using LSTY.SevenDPanel.DependencyInjection;
using LSTY.SevenDPanel.Hosting;
using LSTY.SevenDPanel.Hosting.ServerEvents;
using Microsoft.Extensions.DependencyInjection;

namespace LSTY.SevenDPanel.Tests.Bootstrap
{
    [Serializable]
    public sealed class ProductionCompositionCharacterizationSnapshot
    {
        public string RuntimeChain { get; set; } = string.Empty;
        public string[] GeoIpProviders { get; set; } = Array.Empty<string>();
        public bool SqliteConnectionFactoryRegistrationIsUnique { get; set; }
        public bool SqliteConnectionFactoryIsRootSingleton { get; set; }
        public bool SqliteConnectionFactoryIsSharedAcrossScopes { get; set; }
        public bool RootResolveOfScopedServiceFails { get; set; }
    }

    [Serializable]
    public sealed class ProductionWebRequestScopeSnapshot
    {
        public bool SameServiceIsReusedWithinScope { get; set; }
        public bool DifferentScopeGetsDifferentService { get; set; }
        public bool RootProviderRemainsUsableAfterScopeDispose { get; set; }
    }

    public sealed class ProductionCompositionIsolationWorker : MarshalByRefObject
    {
        public ProductionCompositionCharacterizationSnapshot CaptureValidatedRuntimeGraph(
            int port)
        {
            var dataDirectory = CreateDataDirectory();
            ServiceProviderRuntime? runtime = null;
            try
            {
                SyntheticGameAssembly.InitializeGameData(dataDirectory);
                runtime = PanelServiceProviderFactory.CreateRuntime(
                    PanelHostOptions.FromBinding(port, "127.0.0.1", "http"),
                    dataDirectory,
                    null,
                    _ => { });
                var provider = GetProvider(runtime);
                var rootRuntime = provider.GetRequiredService<IModRuntime>();
                var firstInner = GetInner(rootRuntime);
                var secondInner = GetInner(firstInner);
                var singleton = provider.GetRequiredService<SqliteConnectionFactory>();

                object firstScopedFactory;
                object secondScopedFactory;
                using (var firstScope = provider.CreateScope())
                using (var secondScope = provider.CreateScope())
                {
                    firstScopedFactory = firstScope.ServiceProvider
                        .GetRequiredService<SqliteConnectionFactory>();
                    secondScopedFactory = secondScope.ServiceProvider
                        .GetRequiredService<SqliteConnectionFactory>();
                }

                var rootResolveOfScopedServiceFails = false;
                try
                {
                    provider.GetRequiredService<ServerEventSseSession>();
                }
                catch (InvalidOperationException)
                {
                    rootResolveOfScopedServiceFails = true;
                }

                return new ProductionCompositionCharacterizationSnapshot
                {
                    RuntimeChain = string.Join(
                        " -> ",
                        rootRuntime.GetType().Name,
                        firstInner.GetType().Name,
                        secondInner.GetType().Name),
                    GeoIpProviders = provider.GetServices<IGeoIpProvider>()
                        .Select(service => service.GetType().Name)
                        .ToArray(),
                    SqliteConnectionFactoryRegistrationIsUnique =
                        provider.GetServices<SqliteConnectionFactory>().Count() == 1,
                    SqliteConnectionFactoryIsRootSingleton =
                        ReferenceEquals(singleton, provider.GetRequiredService<SqliteConnectionFactory>()),
                    SqliteConnectionFactoryIsSharedAcrossScopes =
                        ReferenceEquals(singleton, firstScopedFactory) &&
                        ReferenceEquals(singleton, secondScopedFactory),
                    RootResolveOfScopedServiceFails = rootResolveOfScopedServiceFails
                };
            }
            finally
            {
                try { runtime?.Dispose(); } catch { }
                DeleteDataDirectory(dataDirectory);
            }
        }

        public ProductionWebRequestScopeSnapshot CaptureWebRequestScope(int port)
        {
            var dataDirectory = CreateDataDirectory();
            ServiceProviderRuntime? runtime = null;
            try
            {
                SyntheticGameAssembly.InitializeGameData(dataDirectory);
                runtime = PanelServiceProviderFactory.CreateRuntime(
                    PanelHostOptions.FromBinding(port, "127.0.0.1", "http"),
                    dataDirectory,
                    null,
                    _ => { });
                var provider = GetProvider(runtime);
                using var resolver = new MicrosoftDependencyResolver(provider);
                using var firstScope = resolver.BeginScope();
                using var secondScope = resolver.BeginScope();
                var first = firstScope.GetService(typeof(ServerEventSseSession));
                var firstAgain = firstScope.GetService(typeof(ServerEventSseSession));
                var second = secondScope.GetService(typeof(ServerEventSseSession));

                firstScope.Dispose();
                return new ProductionWebRequestScopeSnapshot
                {
                    SameServiceIsReusedWithinScope =
                        first != null && ReferenceEquals(first, firstAgain),
                    DifferentScopeGetsDifferentService =
                        first != null && second != null && !ReferenceEquals(first, second),
                    RootProviderRemainsUsableAfterScopeDispose =
                        provider.GetRequiredService<SqliteConnectionFactory>() != null
                };
            }
            finally
            {
                try { runtime?.Dispose(); } catch { }
                DeleteDataDirectory(dataDirectory);
            }
        }

        public override object InitializeLifetimeService() => null;

        private static IServiceProvider GetProvider(ServiceProviderRuntime runtime)
        {
            var field = typeof(ServiceProviderRuntime).GetField(
                "serviceProvider",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new InvalidOperationException("The runtime provider field was not found.");
            return (IServiceProvider)field.GetValue(runtime);
        }

        private static IModRuntime GetInner(IModRuntime runtime)
        {
            var field = runtime.GetType().GetField(
                "inner",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new InvalidOperationException("The runtime inner field was not found.");
            return (IModRuntime)field.GetValue(runtime);
        }

        private static string CreateDataDirectory()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-bootstrap-characterization-isolated",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void DeleteDataDirectory(string directory)
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
            var root = Directory.GetParent(directory)?.FullName;
            if (root != null && Directory.Exists(root))
            {
                try { Directory.Delete(root); } catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    internal static class SyntheticGameAssembly
    {
        private static Assembly? syntheticGameAssembly;

        public static void InitializeGameData(string directory)
        {
            var gameAssembly = LoadGameAssembly();
            var gameIo = gameAssembly.GetType("GameIO");
            var gamePrefs = gameAssembly.GetType("GamePrefs");
            var preferenceType = gameAssembly.GetType("EnumGamePrefs");
            if (gameIo == null || gamePrefs == null || preferenceType == null)
                throw new InvalidOperationException("The synthetic game assembly is incomplete.");

            var initializeMethod = gameIo.GetMethod(
                "InitializeUserDataPaths",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);
            if (initializeMethod == null)
                throw new InvalidOperationException("The synthetic GameIO initializer is missing.");
            initializeMethod.Invoke(
                null,
                new object[] { Path.Combine(directory, "game-data") });
            SetGamePreference(gamePrefs, preferenceType, "GameWorld", "Navezgane");
            SetGamePreference(gamePrefs, preferenceType, "GameName", "isolated-composition");
            SetGamePreference(gamePrefs, preferenceType, "GameVersion", "v3.0.1-b4");
            SetGamePreference(gamePrefs, preferenceType, "GameSaveStorageType", 0);
        }

        private static Assembly LoadGameAssembly()
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .SingleOrDefault(assembly => assembly.GetName().Name == "Assembly-CSharp");
            if (loaded != null) return loaded;
            syntheticGameAssembly = CreateGameAssembly();
            AppDomain.CurrentDomain.AssemblyResolve += ResolveSyntheticGameAssembly;
            return syntheticGameAssembly;
        }

        private static Assembly? ResolveSyntheticGameAssembly(
            object? sender,
            ResolveEventArgs arguments) =>
            arguments.Name.StartsWith(
                "Assembly-CSharp,",
                StringComparison.Ordinal) ? syntheticGameAssembly : null;

        private static Assembly CreateGameAssembly()
        {
            var assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(
                new AssemblyName("Assembly-CSharp")
                {
                    Version = new Version(0, 0, 0, 0)
                },
                AssemblyBuilderAccess.Run);
            var module = assembly.DefineDynamicModule("Assembly-CSharp");
            var preferences = module.DefineEnum(
                "EnumGamePrefs",
                TypeAttributes.Public,
                typeof(int));
            preferences.DefineLiteral("GameName", 31);
            preferences.DefineLiteral("GameWorld", 33);
            preferences.DefineLiteral("GameVersion", 34);
            preferences.DefineLiteral("GameSaveStorageType", 294);
            var preferenceType = preferences.CreateType();

            DefineGameIo(module);
            DefineGamePrefs(module, preferenceType);
            DefineClientInfo(module);
            DefinePlayerDataFile(module);
            return assembly;
        }

        private static void DefineClientInfo(ModuleBuilder module)
        {
            var clientInfo = module.DefineType(
                "ClientInfo",
                TypeAttributes.Public | TypeAttributes.Class);
            var deviceType = clientInfo.DefineNestedType(
                "EDeviceType",
                TypeAttributes.NestedPublic | TypeAttributes.Sealed,
                typeof(Enum));
            deviceType.DefineField(
                "value__",
                typeof(int),
                FieldAttributes.Private |
                FieldAttributes.SpecialName |
                FieldAttributes.RTSpecialName);
            deviceType.CreateType();
            clientInfo.CreateType();
        }

        private static void DefinePlayerDataFile(ModuleBuilder module)
        {
            var playerDataFile = module.DefineType(
                "PlayerDataFile",
                TypeAttributes.Public | TypeAttributes.Class);
            playerDataFile.CreateType();
        }

        private static void DefineGameIo(ModuleBuilder module)
        {
            var gameIo = module.DefineType(
                "GameIO",
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            var saveDirectory = gameIo.DefineField(
                "saveDirectory",
                typeof(string),
                FieldAttributes.Private | FieldAttributes.Static);
            var initialize = gameIo.DefineMethod(
                "InitializeUserDataPaths",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                new[] { typeof(string) });
            var initializeIl = initialize.GetILGenerator();
            initializeIl.Emit(OpCodes.Ldarg_0);
            initializeIl.Emit(OpCodes.Stsfld, saveDirectory);
            initializeIl.Emit(OpCodes.Ret);
            var getSaveDirectory = gameIo.DefineMethod(
                "GetSaveGameDir",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(string),
                Type.EmptyTypes);
            var getSaveDirectoryIl = getSaveDirectory.GetILGenerator();
            getSaveDirectoryIl.Emit(OpCodes.Ldsfld, saveDirectory);
            getSaveDirectoryIl.Emit(OpCodes.Ret);
            gameIo.CreateType();
        }

        private static void DefineGamePrefs(ModuleBuilder module, Type preferenceType)
        {
            var gamePrefs = module.DefineType(
                "GamePrefs",
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            var world = gamePrefs.DefineField(
                "world",
                typeof(string),
                FieldAttributes.Private | FieldAttributes.Static);
            var name = gamePrefs.DefineField(
                "name",
                typeof(string),
                FieldAttributes.Private | FieldAttributes.Static);
            var version = gamePrefs.DefineField(
                "version",
                typeof(string),
                FieldAttributes.Private | FieldAttributes.Static);
            DefineStringPreferenceSetter(
                gamePrefs,
                preferenceType,
                new[] { (31, name), (33, world), (34, version) });
            var setInteger = gamePrefs.DefineMethod(
                "Set",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                new[] { preferenceType, typeof(int) });
            setInteger.GetILGenerator().Emit(OpCodes.Ret);
            DefineStringPreferenceGetter(
                gamePrefs,
                preferenceType,
                new[] { (31, name), (33, world), (34, version) });
            gamePrefs.CreateType();
        }

        private static void DefineStringPreferenceSetter(
            TypeBuilder gamePrefs,
            Type preferenceType,
            (int Key, FieldBuilder Value)[] fields)
        {
            var method = gamePrefs.DefineMethod(
                "Set",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                new[] { preferenceType, typeof(string) });
            var il = method.GetILGenerator();
            foreach (var field in fields)
            {
                var next = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldc_I4, field.Key);
                il.Emit(OpCodes.Bne_Un_S, next);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stsfld, field.Value);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(next);
            }
            il.Emit(OpCodes.Ret);
        }

        private static void DefineStringPreferenceGetter(
            TypeBuilder gamePrefs,
            Type preferenceType,
            (int Key, FieldBuilder Value)[] fields)
        {
            var method = gamePrefs.DefineMethod(
                "GetString",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(string),
                new[] { preferenceType });
            var il = method.GetILGenerator();
            foreach (var field in fields)
            {
                var next = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldc_I4, field.Key);
                il.Emit(OpCodes.Bne_Un_S, next);
                il.Emit(OpCodes.Ldsfld, field.Value);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(next);
            }
            il.Emit(OpCodes.Ldstr, string.Empty);
            il.Emit(OpCodes.Ret);
        }

        private static void SetGamePreference(
            Type gamePrefs,
            Type preferenceType,
            string preferenceName,
            object value)
        {
            var preference = Enum.Parse(preferenceType, preferenceName);
            var setMethod = gamePrefs.GetMethod(
                "Set",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { preferenceType, value.GetType() },
                null);
            if (setMethod == null)
                throw new InvalidOperationException("The synthetic GamePrefs setter is missing.");
            setMethod.Invoke(null, new[] { preference, value });
        }
    }
}

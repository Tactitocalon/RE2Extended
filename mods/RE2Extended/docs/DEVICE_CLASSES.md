# Device / block classes in the active RE2 config

Source: pinned Reforged Eden 2 Workshop ID `3143225812`, `Content/Configuration/BlocksConfig.ecf`.

Third-party data note: this page records derived interoperability facts (ECF
`Class:` names, block names, IDs and counts) taken from the Reforged Eden 2 mod.
It is not a copy of RE2 source or assets and is not redistributed as RE2 content.
Review RE2's license before reusing anything beyond these reference facts.

These are **ECF `Class:` values**, not native C# type names. A block with no `Class:` is a decoration/terrain/plain block, not a device. Counts are block definitions in RE2; RE2 adds entries on top of vanilla.

| Class | Blocks | Representative block names |
| --- | ---: | --- |
| `AIDroneBay` | 3 | `AIDroneBay`, `DroneBayLegacy`, `Eden_DroneBaySwarm` |
| `ArmorLocker` | 2 | `ArmorLocker`, `ArmorLockerSV` |
| `ATM` | 1 | `ATM` |
| `AutoMiner` | 2 | `AutoMiningDeviceT1`, `Eden_AutoMiningDeviceT4` |
| `CockpitMS` | 3 | `CockpitMS01`, `CockpitMS02`, `CaptainChair01` |
| `CockpitSS` | 10 | `CockpitSV`, `CockpitSV_fighter`, `CockpitSV_fighter_Noscale`, `CockpitSV_ShortRange`, `CockpitSV02`, … |
| `ConstructorBig` | 2 | `ConstructorMS`, `ConstructorT1` |
| `ConstructorBigV2` | 2 | `ConstructorT1V2`, `ConstructorAlienT1` |
| `ConstructorHover` | 4 | `ConstructorSV_Old`, `ConstructorSmallV2`, `ConstructorSV`, `ConstructorHV` |
| `ConstructorRecursive` | 3 | `ConstructorRecursive`, `ConstructorT2`, `ConstructorAlienT2` |
| `ConstructorSmallV2` | 1 | `ConstructorT3` |
| `ConstructorSurvival` | 1 | `ConstructorSurvivalV2` |
| `ConstructorSurvivalV2` | 1 | `ConstructorSurvival` |
| `ConstructorT0` | 2 | `ConstructorT0`, `ConstructorAlienT0` |
| `Container` | 53 | `SciencelabMS`, `ContainerMS01`, `ContainerUltraRare`, `FridgeSV`, `AlienContainer`, … |
| `ContainerController` | 6 | `ContainerControllerLarge`, `ContainerHarvestControllerSmall`, `ContainerHarvestControllerLarge`, `ContainerAmmoControllerSmall`, `ContainerAmmoControllerLarge`, … |
| `ContainerExtension` | 3 | `ContainerExtensionLarge`, `CrewStorage01`, `QuantumContainerExtensionLarge` |
| `ContainerPersonal` | 1 | `ContainerPersonal` |
| `Core` | 8 | `Core`, `CoreNPC`, `CoreT2Small`, `CoreT2Large`, `CoreT3Small`, … |
| `CropsGrown` | 70 | `AlienPlant05`, `AlienPlant07`, `AlienPlant11`, `PlantHarvestTemplate`, `AlienPepperStage5`, … |
| `Damage` | 2 | `Eden_LaserFence1x1`, `Eden_PoisonGasCloud` |
| `DecalControl` | 2 | `DecalsControlBlock`, `Eden_DecalInvisible` |
| `Deconstructor` | 2 | `Deconstructor`, `DeconstructorAlien` |
| `Door` | 23 | `DoorMS01`, `DoorSS01`, `RampTemplate`, `DoorArmored`, `HangarDoor10x5`, … |
| `Elevator` | 2 | `ElevatorMS`, `SVLadder2x1` |
| `Explosive` | 8 | `ExplosiveBlocks`, `ExplosiveBlockFull`, `ExplosiveBlock2Full`, `ConcreteFullDestroyedRemoteEffect`, `HullFullLargeDestroyedRemoteEffect`, … |
| `FieldRepairStation` | 1 | `AutoRepairDevice` |
| `FoodProcessor` | 1 | `FoodProcessorMS` |
| `FoodProcessorV2` | 2 | `FoodProcessorV2`, `FoodProcessorSmall` |
| `FuelTank` | 6 | `FuelTankMSSmall`, `FuelTankSV`, `FuelTankPOI`, `FuelTankSVSmall`, `CrewEngineer03`, … |
| `Furnace` | 2 | `Furnace`, `FurnaceCV` |
| `Generator` | 14 | `GravityGeneratorMS`, `GeneratorSV`, `GeneratorMS`, `GeneratorSVSmall`, `GeneratorBA`, … |
| `Ladder` | 1 | `LadderBlocks` |
| `LandClaimDevice` | 1 | `LandClaimDevice` |
| `LandingGear` | 3 | `LandinggearSV`, `LandinggearMSHeavy`, `LandinggearSingle` |
| `Light` | 8 | `LightSS01`, `LightMS01`, `LightPlant01`, `Flare`, `CameraBlock`, … |
| `LightBarrier` | 2 | `LightBarrierSV`, `LightBarrier` |
| `LightSpot` | 2 | `SpotlightSSCube`, `SpotlightT2` |
| `MedicStation` | 2 | `MedicinelabMS`, `MedicStationHV` |
| `MotionSensor` | 2 | `MotionSensorSV`, `MotionSensor` |
| `NPCDialogue` | 51 | `ConsoleTrading`, `ConsoleMS01`, `NPCAlienTemplate`, `NPCHumanTemplate`, `GeneralPurposeConsole`, … |
| `O2Generator` | 1 | `OxygenGenerator` |
| `O2GeneratorSmall` | 1 | `OxygenGeneratorSmall` |
| `O2H2Generator` | 1 | `OxygenHydrogenGenerator` |
| `O2Station` | 3 | `OxygenStation`, `OxygenStationSS`, `OxygenStationSV` |
| `O2Tank` | 4 | `OxygenTankMS`, `OxygenTankSV`, `oxygenTankEscapePod`, `OxygenTankSmallMS` |
| `PassengerSeat` | 20 | `PassengerSeatMS`, `PassengerSeatSS_Cube`, `Sofa`, `KitchenTable`, `ControlStation`, … |
| `PlantGrowing` | 88 | `PlantGrowingTemplate`, `AlienPepperStage2`, `AlienPepperStage3`, `AlienPepperStage4`, `AlienPepperStage5NoFruit`, … |
| `Portal` | 4 | `Portal`, `TechPortal`, `PortalRing3x3`, `PortalRing5x5` |
| `RemoteConnector` | 4 | `RemoteConnection`, `CrewBridge01`, `RemoteConnectionT2`, `RemoteConnectionT3` |
| `RepairBay` | 1 | `RepairBayBA` |
| `RepairBayConsole` | 1 | `RepairBayConsole` |
| `RepairStation` | 1 | `RepairStation` |
| `Sensor` | 1 | `TriggerPlate` |
| `ShieldGenerator` | 12 | `ShieldGeneratorBA`, `ShieldGeneratorSVT2`, `ShieldGeneratorHVT2`, `ShieldGeneratorT0`, `ShieldGeneratorPlanetZirax`, … |
| `Sign` | 1 | `LCDNoFrame1x1` |
| `Sleeping` | 7 | `SurvivalTent`, `Bed`, `BunkBed`, `ScifiBed`, `BunkBed02`, … |
| `SolarGenerator` | 1 | `SolarGenerator` |
| `SolarPanel` | 4 | `SolarPanelBlocks`, `SolarPanelSlope`, `SolarPanelSmallBlocks`, `SolarPanelSlope3Small` |
| `Spawner` | 5 | `EntitySpawner1`, `ESD_ComputerStation_C01`, `Eden_SpawnerOrganic`, `LegacySpawnPod`, `Eden_SpawnerGateRed` |
| `SurvivalTent` | 1 | `SurvivalTent01` |
| `Switch` | 6 | `ConsoleSmallMS01`, `LeverSV`, `Lever`, `Eden_ButtonPanel`, `SwitchLegacy`, … |
| `Teleporter` | 5 | `TeleporterCV`, `TeleporterBA`, `TeleporterCVT2`, `TeleporterBAT2`, `TeleporterBAT3` |
| `Thruster` | 47 | `ThrusterSVRoundNormal`, `ThrusterMSRoundArmored`, `ThrusterSVDirectional`, `ThrusterMSDirectional`, `ThrusterMSRoundNormal2x2`, … |
| `TraderNPC` | 1 | `Trader` |
| `TradingStation` | 1 | `TradingStation` |
| `TrapDoorOneTime` | 2 | `ScifiMine1`, `TrapDoor` |
| `TurretAuto` | 14 | `TurretIONCannon2`, `SentryGun01`, `SentryGun02`, `SentryGun04`, `TurretLegacyDisruptor`, … |
| `TurretManual` | 105 | `TurretTemplate`, `TurretBaseCannonOld`, `TurretDrillTemplate`, `TurretGVLaser`, `TurretIONCannon`, … |
| `Vent` | 1 | `Ventilator` |
| `WarpDrive` | 5 | `WarpDrive`, `WarpDriveSV`, `WarpDriveT2`, `WarpDriveSVT2`, `Eden_WarpDriveDebug` |
| `WarpDriveTank` | 4 | `PentaxidTank`, `PentaxidTankSV`, `PentaxidTankT2`, `PentaxidTankSVT2` |
| `WaterGenerator` | 1 | `WaterGenerator` |
| `Weapon` | 60 | `WeaponSV01`, `WeaponSV02`, `WeaponSV03`, `WeaponSV04`, `WeaponSV05`, … |

## Notes

- `BlockTeleporter` is the native C# implementation behind `Class: Teleporter`; it is not a generic device class.
- `Class: Light` is the known discoverable class used by the test mod, but it carries light semantics.
- `Class: Devices` is not an active class in this config; it appears only as a commented-out example.
- Class-less blocks can still be `IsActivateable` and powered. Runtime testing showed they are **not registered in the custom-device position registry**, but `IsAccessible: true` is what makes the device show in the block/control-panel list. The Cruise Amplifier uses `Group: cpgSignal`, `IsAccessible: true`, `IsActivateable: true`, `IsActivateableInCP: true`, no `Class:`, and a block-ID scan fallback for detection. The `CapacitorMS` model and explicit `2,1,1` size replace the decorative base values.
- Decorative consoles are usually `Class: NPCDialogue` when they are interactive (for example `ConsoleLargeMS01`, `ConsoleMS01`, `ComputerStation_C01`); inherited variants such as `ComputerStation_C02` keep that class through `Ref`. Many pure deco consoles have no class (`Eden_ServerConsole01`, `Eden_WallDisplay01`, `ConsoleDesk_C03..C05`). `ConsoleSmallMS01` is `Class: Switch`, not `NPCDialogue`.
- There is no obvious generic passive/active device class. A new device should either use the least intrusive existing class after a runtime probe, or a mod-defined class if that can be registered safely.
- `Damage`, `Explosive`, `Spawner`, `TrapDoorOneTime` and similar classes are gameplay/effect classes, not general-purpose powered devices.

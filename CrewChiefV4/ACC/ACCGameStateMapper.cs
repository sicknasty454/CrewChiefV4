﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CrewChiefV4.GameState;
using CrewChiefV4.ACC;
using CrewChiefV4.ACC.Data;
using System.Runtime.InteropServices;
namespace CrewChiefV4.ACC
{
    class ACCGameStateMapper : GameStateMapper
    {

        RaceSessionType previousRaceSessionType = RaceSessionType.FreePractice1;
        RaceSessionPhase previousRaceSessionPhase = RaceSessionPhase.RaceSessionPhase_Max;
           
        // next track conditions sample due after:
        private DateTime nextConditionsSampleDue = DateTime.MinValue;

        private void PrintProperties<T>(T myObj)
        {
            /*foreach (var prop in myObj.GetType().GetProperties())
            {
                Console.WriteLine(Marshal.OffsetOf(typeof(T), prop.Name).ToString("d") + " prop " + prop.Name + ": " + prop.GetValue(myObj, null));
            }*/
            Console.WriteLine("sizeOf: " + myObj.ToString() + " " + Marshal.SizeOf(typeof(T)).ToString("X"));
            foreach (var field in myObj.GetType().GetFields())
            {
                Console.WriteLine("0x" + Marshal.OffsetOf(typeof(T), field.Name).ToString("X") + " " + field.Name + ": " + field.GetValue(myObj));
            }
        }
        public ACCGameStateMapper()
        {

        }

        public override void versionCheck(Object memoryMappedFileStruct)
        {
            // no version data in the stream so this is a no-op

        }
        public override void setSpeechRecogniser(SpeechRecogniser speechRecogniser)
        {
            speechRecogniser.addiRacingSpeechRecogniser();
            this.speechRecogniser = speechRecogniser;
        }
        public float mapToFloatTime(int time)
        {
            TimeSpan ts = TimeSpan.FromTicks(time);
            return (float)ts.TotalMilliseconds * 10;
        }
        public override GameStateData mapToGameStateData(Object structWrapper, GameStateData previousGameState)
        {
            ACCSharedMemoryReader.ACCStructWrapper wrapper = (ACCSharedMemoryReader.ACCStructWrapper)structWrapper;            
            GameStateData currentGameState = new GameStateData(wrapper.ticksWhenRead);
            ACCSharedMemoryData data = wrapper.data;
            
            if(data.isReady != 1)
            {
                return previousGameState;
            }
            if (!previousRaceSessionType.Equals(data.sessionData.currentSessionType))
            {   
                PrintProperties<CrewChiefV4.ACC.Data.Track>(data.track);
                PrintProperties<CrewChiefV4.ACC.Data.WeatherStatus>(data.track.weatherState);
                previousRaceSessionType = data.sessionData.currentSessionType;
                Console.WriteLine("currentSessionType " + data.sessionData.currentSessionType);
            }
            if (!previousRaceSessionPhase.Equals(data.sessionData.currentSessionPhase))            
            {
                PrintProperties<CrewChiefV4.ACC.Data.ACCSessionData>(data.sessionData);
                Console.WriteLine("previousRaceSessionPhase " + previousRaceSessionPhase );
                Console.WriteLine("currentRaceSessionPhase " + data.sessionData.currentSessionPhase);
            }
            
            //Console.WriteLine("tyre temp" + wrapper.physicsData.tyreTempM[0]);
            SessionType previousSessionType = SessionType.Unavailable;
            SessionPhase previousSessionPhase = SessionPhase.Unavailable;
            float previousSessionRunningTime = -1;
            if(previousGameState != null)
            {
                previousSessionType = previousGameState.SessionData.SessionType;
                previousSessionRunningTime = previousGameState.SessionData.SessionRunningTime;
                previousSessionPhase = previousGameState.SessionData.SessionPhase;
            }
            //test commit
            SessionType currentSessionType = mapToSessionType(data.sessionData.currentSessionType);
            currentGameState.SessionData.SessionType = currentSessionType;
            SessionPhase currentSessioPhase = mapToSessionPhase(data.sessionData.currentSessionPhase, currentSessionType);
            currentGameState.SessionData.SessionPhase = currentSessioPhase;

            currentGameState.SessionData.SessionRunningTime = (float)TimeSpan.FromMilliseconds((data.sessionData.physicsTime - data.sessionData.sessionStartTimeStamp)).TotalSeconds;
            currentGameState.SessionData.SessionTotalRunTime = (float)data.sessionData.sessionDuration;
            currentGameState.SessionData.SessionTimeRemaining = (float)TimeSpan.FromMilliseconds((data.sessionData.sessionEndTime - data.sessionData.physicsTime)).TotalSeconds;
            
            currentGameState.SessionData.SessionHasFixedTime = true;
            Driver playerDriver = data.playerDriver;
            currentGameState.SessionData.NumCarsOverall = data.driverCount;
            //this still needs fixing
            if (currentSessionType != SessionType.Unavailable && 
                (previousRaceSessionPhase == RaceSessionPhase.StartingUI && data.sessionData.currentSessionPhase == RaceSessionPhase.PreFormationTime && currentSessionType == SessionType.Race))
            {
                currentGameState.SessionData.IsNewSession = true;
                PrintProperties<CrewChiefV4.ACC.Data.ACCSessionData>(data.sessionData);
                //Console.WriteLine("New session, trigger data:");
                Console.WriteLine("SessionTimeRemaining = " + currentGameState.SessionData.SessionTimeRemaining);
                Console.WriteLine("SessionTotalRunTime = " + currentGameState.SessionData.SessionTotalRunTime);
                Console.WriteLine("SessionRunningTime = " + currentGameState.SessionData.SessionRunningTime);
                currentGameState.SessionData.NumCarsOverallAtStartOfSession = data.driverCount;

                currentGameState.SessionData.TrackDefinition = TrackData.getTrackDefinition(data.track.name, 0, data.track.length);
                if (previousGameState != null && previousGameState.SessionData.TrackDefinition != null)
                {
                    if (previousGameState.SessionData.TrackDefinition.name.Equals(currentGameState.SessionData.TrackDefinition.name))
                    {
                        if (previousGameState.hardPartsOnTrackData.hardPartsMapped)
                        {
                            currentGameState.hardPartsOnTrackData.processedHardPartsForBestLap = previousGameState.hardPartsOnTrackData.processedHardPartsForBestLap;
                            currentGameState.hardPartsOnTrackData.isAlreadyBraking = previousGameState.hardPartsOnTrackData.isAlreadyBraking;
                            currentGameState.hardPartsOnTrackData.hardPartStart = previousGameState.hardPartsOnTrackData.hardPartStart;
                            currentGameState.hardPartsOnTrackData.hardPartsMapped = previousGameState.hardPartsOnTrackData.hardPartsMapped;
                        }
                    }
                }
                TrackDataContainer tdc = TrackData.TRACK_LANDMARKS_DATA.getTrackDataForTrackName(data.track.name, currentGameState.SessionData.TrackDefinition.trackLength);
                currentGameState.SessionData.TrackDefinition.trackLandmarks = tdc.trackLandmarks;
                if (tdc.isDefinedInTracklandmarksData)
                {
                    currentGameState.SessionData.TrackDefinition.isOval = tdc.isOval;
                }
                else
                {
                    currentGameState.SessionData.TrackDefinition.isOval = false;
                }
                currentGameState.SessionData.TrackDefinition.setGapPoints();
                GlobalBehaviourSettings.UpdateFromTrackDefinition(currentGameState.SessionData.TrackDefinition);
                currentGameState.OpponentData.Clear();
                currentGameState.SessionData.PlayerLapData.Clear();
                currentGameState.SessionData.SessionStartTime = currentGameState.Now;

                currentGameState.SessionData.LeaderHasFinishedRace = false;
                currentGameState.PitData.IsRefuellingAllowed = true;
                currentGameState.SessionData.SessionHasFixedTime = true;

                currentGameState.PitData.InPitlane = playerDriver.trackLocation == CarLocation.ECarLocation__Track;
                currentGameState.PositionAndMotionData.DistanceRoundTrack = Math.Abs(playerDriver.distanceRoundTrack * currentGameState.SessionData.TrackDefinition.trackLength);

                //TODO update car classes shuold be easy as they will all be GT3 :D
                currentGameState.carClass = CarData.getCarClassFromEnum(CarData.CarClassEnum.GT3);
                GlobalBehaviourSettings.UpdateFromCarClass(currentGameState.carClass);
                Console.WriteLine("Player is using car class " + currentGameState.carClass.getClassIdentifier());


                currentGameState.SessionData.DeltaTime = new DeltaTime(currentGameState.SessionData.TrackDefinition.trackLength, currentGameState.PositionAndMotionData.DistanceRoundTrack, currentGameState.Now);
                currentGameState.SessionData.SectorNumber = (int)playerDriver.currentSector + 1;

                for(int i = 1; i < data.driverCount; i++)
                {
                    Driver driver = data.drivers[i];
                        currentGameState.OpponentData.Add(driver.name, createOpponentData(driver, true,
                            currentGameState.SessionData.TrackDefinition.trackLength));
                }
                // add a conditions sample when we first start a session so we're not using stale or default data in the pre-lights phase
                currentGameState.Conditions.addSample(currentGameState.Now, 0, 1, data.track.weatherState.ambientTemperature, data.track.weatherState.roadTemperature, 
                    data.track.weatherState.rainLevel, data.track.weatherState.windSpeed, 0, 0, 0, true);
            }
            else if(currentSessionType != SessionType.Unavailable)
            {
                if (previousSessionPhase != currentGameState.SessionData.SessionPhase)
                {

                }
            }
            previousRaceSessionPhase = data.sessionData.currentSessionPhase;
            //currentGameState.SessionData.SessionPhase = SessionPhase.Green;
            return currentGameState;
        }


        private OpponentData createOpponentData(Driver driver, Boolean loadDriverName, float trackLength)
        {
            String driverName = driver.name.ToLower();
            if (loadDriverName && CrewChief.enableDriverNames)
            {
                speechRecogniser.addNewOpponentName(driverName);
            }
            OpponentData opponentData = new OpponentData();
            opponentData.IsActive = true;
            opponentData.DriverRawName = driverName;
            opponentData.OverallPosition = driver.position;
            opponentData.CompletedLaps = driver.lapCount;
            opponentData.DistanceRoundTrack = driver.distanceRoundTrack * trackLength;
            opponentData.DeltaTime = new DeltaTime(trackLength, opponentData.DistanceRoundTrack, DateTime.UtcNow);
            opponentData.CarClass = CarData.getCarClassFromEnum(CarData.CarClassEnum.GT3);
            opponentData.CurrentSectorNumber = (int)driver.currentSector + 1;
            return opponentData;
        }
        private SessionType mapToSessionType(RaceSessionType sessionType)
        {
            switch (sessionType)
            {
                case RaceSessionType.FreePractice1:
                case RaceSessionType.FreePractice2:
                    return SessionType.Practice;
                case RaceSessionType.Hotstint:
                case RaceSessionType.Hotlap:
                    return SessionType.HotLap;
                case RaceSessionType.PreQualifying:
                case RaceSessionType.Qualifying:
                case RaceSessionType.Qualifying1:
                case RaceSessionType.Qualifying3:
                case RaceSessionType.Qualifying4:
                case RaceSessionType.Superpole:
                case RaceSessionType.HotlapSuperpole:
                    return SessionType.Qualify;
                case RaceSessionType.Race:
                    return SessionType.Race;        
            }
            return SessionType.Unavailable;
        }
        private SessionPhase mapToSessionPhase(RaceSessionPhase currentRaceSessionPhase, SessionType currentSessionType)
        {
            switch(currentSessionType)
            {
                case SessionType.Practice:
                case SessionType.HotLap:
                case SessionType.Qualify:
                        return SessionPhase.Green;
                case SessionType.Race:
                    {
                        switch(currentRaceSessionPhase)
                        {
                            case RaceSessionPhase.StartingUI:
                                return SessionPhase.Garage;
                            case RaceSessionPhase.PreFormationTime:
                            case RaceSessionPhase.FormationTime: //here we are in our car on the grid waition to roll
                                return SessionPhase.Gridwalk;
                            case RaceSessionPhase.PreSessionTime:                            
                                return SessionPhase.Formation;
                            case RaceSessionPhase.SessionTime:
                                return SessionPhase.Green;
                            case RaceSessionPhase.SessionOverTime:
                                return SessionPhase.Checkered;
                            case RaceSessionPhase.ResultUI:
                            case RaceSessionPhase.PostSessionTime:
                                return SessionPhase.Finished;
                            default:
                                return SessionPhase.Unavailable;
                        }
                    }
                default:
                    return SessionPhase.Unavailable;
            }
        }
    }
}

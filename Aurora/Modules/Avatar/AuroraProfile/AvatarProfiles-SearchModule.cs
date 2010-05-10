/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Net;
using System.Net.Sockets;
using log4net;
using Nini.Config;
using OpenMetaverse;
using ProfileFlags = OpenMetaverse.ProfileFlags;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using Nwc.XmlRpc;
using System.Xml;
using Aurora.Framework;
using Aurora.DataManager;
using OpenSim.Services.Interfaces;
using OpenSim.Server.Base;
using FriendInfo = OpenSim.Services.Interfaces.FriendInfo;
using OpenSim.Region.DataSnapshot.Interfaces;
using Aurora.DataManager.Frontends;

namespace Aurora.Modules
{
    public class AuroraProfileModule : ISharedRegionModule
    {
        #region Declares

        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private Scene m_scene;
        private IConfigSource m_config;
        private Dictionary<string, Dictionary<UUID, string>> ClassifiedsCache = new Dictionary<string, Dictionary<UUID, string>>();
        private Dictionary<string, List<string>> ClassifiedInfoCache = new Dictionary<string, List<string>>();
        private ProfileFrontend ProfileFrontend = null;
        private IProfileData ProfileData = null;
        private IGenericData GenericData = null;
        private IRegionData RegionData = null;
        private IConfigSource m_gConfig;
        private List<Scene> m_Scenes = new List<Scene>();
        private bool m_SearchEnabled = true;
        private bool m_ProfileEnabled = true;
        protected IFriendsService m_FriendsService = null;
        protected IGroupsModule GroupsModule = null;
        private System.Timers.Timer aTimer = null;
        protected double parserTime = 3600000;
        private IDataSnapshot DataSnapShotManager;

        #endregion

        #region IRegionModule Members

        public void Initialise(IConfigSource config)
        {
            m_config = config;
            m_gConfig = config;
            IConfig profileConfig = config.Configs["Profile"];
            if (profileConfig == null)
            {
                m_log.Info("[AuroraProfileSearch] Not configured, disabling");
                m_SearchEnabled = false;
                return;
            }
            IConfig friendsConfig = config.Configs["Friends"];
            if (friendsConfig != null)
            {
                int mPort = friendsConfig.GetInt("Port", 0);

                string connector = friendsConfig.GetString("Connector", String.Empty);
                Object[] args = new Object[] { config };

                m_FriendsService = ServerUtils.LoadPlugin<IFriendsService>(connector, args);

            }
            parserTime = profileConfig.GetDouble("ParserTime", 3600000);
            if (m_FriendsService == null)
            {
                m_log.Error("[AuroraProfileSearch]: No Connector defined in section Friends, or filed to load, cannot continue");
                m_ProfileEnabled = false;
                m_SearchEnabled = false;
            }
            else if (profileConfig.GetString("ProfileModule", Name) != Name)
            {
                m_ProfileEnabled = false;
            }
            else if (profileConfig.GetString("SearchModule", Name) != Name)
            {
                m_SearchEnabled = false;
            }
        }

        public void AddRegion(Scene scene)
        {
            IConfig AuroraDataConfig = m_gConfig.Configs["AuroraData"];
            string connectionString = AuroraDataConfig.GetString("RemoteConnectionStrings", "");
            if (connectionString == "")
                ProfileFrontend = new ProfileFrontend(false, "");
            else
                ProfileFrontend = new ProfileFrontend(true, connectionString);

            if (!m_Scenes.Contains(scene))
                m_Scenes.Add(scene);
            m_scene = scene;
            m_scene.EventManager.OnNewClient += NewClient;
        }

        public void RemoveRegion(Scene scene)
        {

        }

        public void RegionLoaded(Scene scene)
        {
            ProfileData = Aurora.DataManager.DataManager.GetDefaultProfilePlugin();
            GenericData = Aurora.DataManager.DataManager.GetDefaultGenericPlugin();
            RegionData = Aurora.DataManager.DataManager.GetDefaultRegionPlugin();
            GroupsModule = m_scene.RequestModuleInterface<IGroupsModule>();
            DataSnapShotManager = m_scene.RequestModuleInterface<IDataSnapshot>();
            if (m_SearchEnabled && DataSnapShotManager != null)
                StartSearch();
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
        }

        public string Name
        {
            get { return "AuroraProfileSearch"; }
        }

        public bool IsSharedModule
        {
            get { return false; }
        }

        #endregion

        #region Client

        public void NewClient(IClientAPI client)
        {
            IUserProfileInfo userProfile = ProfileFrontend.GetUserProfile(client.AgentId);
            if (userProfile == null)
                ProfileFrontend.CreateNewProfile(client.AgentId, client.FirstName, client.LastName);

            if (m_ProfileEnabled)
            {
                client.OnRequestAvatarProperties += RequestAvatarProperty;
                client.OnUpdateAvatarProperties += UpdateAvatarProperties;
                client.AddGenericPacketHandler("avatarclassifiedsrequest", HandleAvatarClassifiedsRequest);
                client.OnClassifiedInfoRequest += ClassifiedInfoRequest;
                client.OnClassifiedInfoUpdate += ClassifiedInfoUpdate;
                client.OnClassifiedDelete += ClassifiedDelete;
                client.OnClassifiedGodDelete += GodClassifiedDelete;
                client.OnUserInfoRequest += UserPreferencesRequest;
                client.OnUpdateUserInfo += UpdateUserPreferences;
                // Notes
                client.AddGenericPacketHandler("avatarnotesrequest", HandleAvatarNotesRequest);
                client.OnAvatarNotesUpdate += AvatarNotesUpdate;

                //Profile
                client.OnAvatarInterestUpdate += AvatarInterestsUpdate;

                // Picks
                client.AddGenericPacketHandler("avatarpicksrequest", HandleAvatarPicksRequest);
                client.AddGenericPacketHandler("pickinforequest", HandlePickInfoRequest);
                client.OnPickInfoUpdate += PickInfoUpdate;
                client.OnPickDelete += PickDelete;
                client.OnPickGodDelete += GodPickDelete;
            }
            if(m_SearchEnabled)
            {
                // Subscribe to messages
                client.OnDirPlacesQuery += DirPlacesQuery;
                client.OnDirFindQuery += DirFindQuery;
                client.OnDirPopularQuery += DirPopularQuery;
                client.OnDirLandQuery += DirLandQuery;
                client.OnDirClassifiedQuery += DirClassifiedQuery;
                // Response after Directory Queries
                client.OnEventInfoRequest += EventInfoRequest;
                client.OnMapItemRequest += HandleMapItemRequest;
                client.OnPlacesQuery += OnPlacesQueryRequest;
            }
        }

        public void RemoveClient(IClientAPI client)
        {
            client.OnRequestAvatarProperties -= RequestAvatarProperty;
            client.OnUpdateAvatarProperties -= UpdateAvatarProperties;
        }

        #endregion

        #region Helpers

        private bool IsFriendOfUser(UUID friend, UUID requested)
        {
            OpenSim.Services.Interfaces.FriendInfo[] friendList = m_FriendsService.GetFriends(requested);
            if (friend == requested)
                return true;

            foreach (OpenSim.Services.Interfaces.FriendInfo item in friendList)
            {
                if (item.PrincipalID == friend)
                {
                    return true;
                }
            }
            ScenePresence sp = m_scene.GetScenePresence(friend);
            if (sp.GodLevel != 0)
                return true;
            return false;
        }

        #endregion

        #region Profile Module

        public void HandleAvatarClassifiedsRequest(Object sender, string method, List<String> args)
        {
            if (!(sender is IClientAPI))
                return;

            IClientAPI remoteClient = (IClientAPI)sender;
            Dictionary<UUID, string> classifieds = new Dictionary<UUID, string>();
            bool isFriend = IsFriendOfUser(remoteClient.AgentId, new UUID(args[0]));
            if (isFriend)
            {
                IUserProfileInfo profile = ProfileFrontend.GetUserProfile(new UUID(args[0]));
                foreach (Classified classified in profile.Classifieds)
                {
                    classifieds.Add(new UUID(classified.ClassifiedUUID), classified.Name);
                }
                remoteClient.SendAvatarClassifiedReply(new UUID(args[0]), classifieds);
            }
            else
            {
                remoteClient.SendAvatarClassifiedReply(new UUID(args[0]), classifieds);
            }          
        }

        public void ClassifiedInfoRequest(UUID queryClassifiedID, IClientAPI remoteClient)
        {
            Classified classified = ProfileFrontend.ReadClassifiedInfoRow(queryClassifiedID.ToString());
            Vector3 globalPos = new Vector3();
            try
            {
                Vector3.TryParse(classified.PosGlobal, out globalPos);
            }
            catch
            {
                globalPos = new Vector3(128, 128, 128);
            }

            remoteClient.SendClassifiedInfoReply(queryClassifiedID, new UUID(classified.CreatorUUID), Convert.ToUInt32(classified.CreationDate), Convert.ToUInt32(classified.ExpirationDate), Convert.ToUInt32(classified.Category), classified.Name, classified.Description, new UUID(classified.ParcelUUID), Convert.ToUInt32(classified.ParentEstate), new UUID(classified.SnapshotUUID), classified.SimName, globalPos, classified.ParcelName, Convert.ToByte(classified.ClassifiedFlags), Convert.ToInt32(classified.PriceForListing));

        }
        public void ClassifiedInfoUpdate(UUID queryclassifiedID, uint queryCategory, string queryName, string queryDescription, UUID queryParcelID,
                                         uint queryParentEstate, UUID querySnapshotID, Vector3 queryGlobalPos, byte queryclassifiedFlags,
                                         int queryclassifiedPrice, IClientAPI remoteClient)
        {
            string creatorUUID = remoteClient.AgentId.ToString();
            string classifiedUUID = queryclassifiedID.ToString();
            string category = queryCategory.ToString();
            string name = queryName;
            string description = queryDescription;
            string parentestate = queryParentEstate.ToString();
            string snapshotUUID = querySnapshotID.ToString();
            string simname = remoteClient.Scene.RegionInfo.RegionName;
            string globalpos = queryGlobalPos.ToString();
            string classifiedFlags = queryclassifiedFlags.ToString();
            string classifiedPrice = queryclassifiedPrice.ToString();

            ScenePresence p = m_scene.GetScenePresence(remoteClient.AgentId);
            Vector3 avaPos = p.AbsolutePosition;
            string parceluuid = p.currentParcelUUID.ToString();
            Vector3 posGlobal = new Vector3(remoteClient.Scene.RegionInfo.RegionLocX * Constants.RegionSize + avaPos.X, remoteClient.Scene.RegionInfo.RegionLocY * Constants.RegionSize + avaPos.Y, avaPos.Z);
            string pos_global = posGlobal.ToString();
            ILandObject parcel = m_scene.LandChannel.GetLandObject(p.AbsolutePosition.X, p.AbsolutePosition.Y);
            string parcelname = parcel.LandData.Name;
            string creationdate = Util.UnixTimeSinceEpoch().ToString();
            int expirationdt = Util.UnixTimeSinceEpoch() + (365 * 24 * 60 * 60);
            string expirationdate = expirationdt.ToString();

            #region Checks on empty strings

            if (parcelname == "")
            {
                parcelname = "Unknown";
            }
            if (parceluuid == "")
            {
                parceluuid = "00000000-0000-0000-0000-0000000000000";
            }

            if (description == "")
            {
                description = "No Description";
            }

            #endregion

            List<string> values = new List<string>();
            values.Add(classifiedUUID);
            values.Add(creatorUUID);
            values.Add(creationdate);
            values.Add(expirationdate);
            values.Add(category);
            values.Add(name);
            values.Add(description);
            values.Add(parceluuid);
            values.Add(parentestate);
            values.Add(snapshotUUID);
            values.Add(simname);
            values.Add(globalpos);
            values.Add(parcelname);
            values.Add(classifiedFlags);
            values.Add(classifiedPrice);
            GenericData.Insert("classifieds", values.ToArray());
            ProfileFrontend.RemoveFromCache(remoteClient.AgentId);
        }
        public void ClassifiedDelete(UUID queryClassifiedID, IClientAPI remoteClient)
        {
            List<string> keys = new List<string>();
            List<string> values = new List<string>();
            keys.Add("classifieduuid");
            values.Add(queryClassifiedID.ToString());
            GenericData.Delete("classifieds", keys.ToArray(), values.ToArray());
            ProfileFrontend.RemoveFromCache(remoteClient.AgentId);
        }
        public void GodClassifiedDelete(UUID queryClassifiedID, IClientAPI remoteClient)
        {
            ScenePresence sp = m_scene.GetScenePresence(remoteClient.AgentId);
            if (sp.GodLevel != 0)
            {
                List<string> keys = new List<string>();
                List<string> values = new List<string>();
                keys.Add("classifieduuid");
                values.Add(queryClassifiedID.ToString());
                GenericData.Delete("classifieds", keys.ToArray(), values.ToArray());
                ProfileFrontend.RemoveFromCache(remoteClient.AgentId);
            }
        }
        public void HandleAvatarPicksRequest(Object sender, string method, List<String> args)
        {
            if (!(sender is IClientAPI))
            {
                m_log.Debug("sender isnt IClientAPI");
                return;
            }
            IClientAPI remoteClient = (IClientAPI)sender;
            Dictionary<UUID, string> picks = new Dictionary<UUID, string>();
            bool isFriend = IsFriendOfUser(remoteClient.AgentId, new UUID(args[0]));
            if (isFriend)
            {
                IUserProfileInfo profile = ProfileFrontend.GetUserProfile(new UUID(args[0]));

                foreach (ProfilePickInfo pick in profile.Picks)
                {
                    picks.Add(new UUID(pick.pickuuid), pick.name);
                }
                remoteClient.SendAvatarPicksReply(new UUID(args[0]), picks);
            }
            else
            {
                remoteClient.SendAvatarPicksReply(new UUID(args[0]), picks);
            }
        }
        public void HandlePickInfoRequest(Object sender, string method, List<String> args)
        {
            if (!(sender is IClientAPI))
            {
                m_log.Debug("sender isnt IClientAPI");
                return;
            }
            IClientAPI remoteClient = (IClientAPI)sender;
            
            ProfilePickInfo pick = ProfileFrontend.ReadPickInfoRow(args[1]);
            Vector3 globalPos = new Vector3();
            try
            {
                Vector3.TryParse(pick.posglobal, out globalPos);
            }
            catch (Exception ex)
            {
                ex = new Exception();
                globalPos = new Vector3(128, 128, 128);
            }
            bool two = false;
            int ten = 0;
            bool twelve = false;

            try
            {
                two = Convert.ToBoolean(pick.toppick);
                ten = Convert.ToInt32(pick.sortorder);
                twelve = Convert.ToBoolean(pick.enabled);
            }
            catch (Exception ex)
            {
                ex = new Exception();
                two = false;
                ten = 0;
                twelve = true;
            }
            remoteClient.SendPickInfoReply(new UUID(pick.pickuuid), new UUID(pick.creatoruuid), two, new UUID(pick.parceluuid), pick.name, pick.description, new UUID(pick.snapshotuuid), pick.user, pick.originalname, pick.simname, globalPos, ten, twelve);
        }
        public void PickInfoUpdate(IClientAPI remoteClient, UUID pickID, UUID creatorID, bool topPick, string name, string desc, UUID snapshotID, int sortOrder, bool enabled)
        {
            string pick = GenericData.Query(new string[]{"creatoruuid","pickuuid"},new string[]{creatorID.ToString(),pickID.ToString()},"userpicks","pickuuid")[0];
            ScenePresence p = m_scene.GetScenePresence(remoteClient.AgentId);
            Vector3 avaPos = p.AbsolutePosition;

            string parceluuid = p.currentParcelUUID.ToString();
            Vector3 posGlobal = new Vector3(avaPos.X, avaPos.Y, avaPos.Z);

            string pos_global = posGlobal.ToString();

            ILandObject targetlandObj = m_scene.LandChannel.GetLandObject(avaPos.X, avaPos.Y);
            UUID ownerid = targetlandObj.LandData.OwnerID;
            ScenePresence parcelowner = m_scene.GetScenePresence(ownerid);
            string parcelfirst;
            string parcellast;
            try
            {
                parcelfirst = parcelowner.Firstname;
                parcellast = parcelowner.Lastname;

            }
            catch (Exception ex)
            {
                ex = new Exception();
                parcelfirst = "";
                parcellast = "";
            }
            string user = parcelfirst + " " + parcellast;

            string OrigionalName = targetlandObj.LandData.Name;

            #region Checks on empty strings

            if (parceluuid == "")
            {
                parceluuid = "00000000-0000-0000-0000-0000000000000";
            }
            if (desc == "")
            {
                desc = " ";
            }


            #endregion

            if (pick == "")
            {
                List<string> values = new List<string>();
                values.Add(pickID.ToString());
                values.Add(creatorID.ToString());
                values.Add(topPick.ToString());
                values.Add(parceluuid.ToString());
                values.Add(name);
                values.Add(desc);
                values.Add(snapshotID.ToString());
                values.Add(user);
                values.Add(OrigionalName);
                values.Add(remoteClient.Scene.RegionInfo.RegionName);
                values.Add(pos_global);
                values.Add(sortOrder.ToString());
                values.Add(enabled.ToString());
                GenericData.Insert("userpicks", values.ToArray());
            }
            else
            {
                List<string> keys = new List<string>();
                List<string> values = new List<string>();
                keys.Add("parceluuid");
                keys.Add("name");
                keys.Add("snapshotuuid");
                keys.Add("description");
                keys.Add("simname");
                keys.Add("posglobal");
                keys.Add("sortorder");
                keys.Add("enabled");
                values.Add(parceluuid.ToString());
                values.Add(name);
                values.Add(snapshotID.ToString());
                values.Add(desc);
                values.Add(remoteClient.Scene.RegionInfo.RegionName);
                values.Add(pos_global);
                values.Add(sortOrder.ToString());
                values.Add(enabled.ToString());
                List<string> keys2 = new List<string>();
                keys2.Add("pickuuid");
                List<string> values2 = new List<string>();
                values2.Add(pickID.ToString());
                GenericData.Update("userpicks", values.ToArray(), keys.ToArray(), keys2.ToArray(), values2.ToArray());
            }
            ProfileFrontend.RemoveFromCache(remoteClient.AgentId);
        }
        public void GodPickDelete(IClientAPI remoteClient, UUID AgentID, UUID PickID, UUID queryID)
        {
            ScenePresence sp = m_scene.GetScenePresence(remoteClient.AgentId);
            if (sp.GodLevel != 0)
            {
                List<string> keys = new List<string>();
                List<string> values = new List<string>();
                keys.Add("pickuuid");
                values.Add(PickID.ToString());
                GenericData.Delete("userpicks", keys.ToArray(), values.ToArray());
                ProfileFrontend.RemoveFromCache(remoteClient.AgentId);
            }
        }
        public void PickDelete(IClientAPI remoteClient, UUID queryPickID)
        {
            List<string> keys = new List<string>();
            List<string> values = new List<string>();
            keys.Add("pickuuid");
            values.Add(queryPickID.ToString());
            GenericData.Delete("userpicks", keys.ToArray(), values.ToArray());
            ProfileFrontend.RemoveFromCache(remoteClient.AgentId);
        }
        public void HandleAvatarNotesRequest(Object sender, string method, List<String> args)
        {
            if (!(sender is IClientAPI))
            {
                m_log.Debug("sender isnt IClientAPI");
                return;
            }
            IClientAPI remoteClient = (IClientAPI)sender;
            IUserProfileInfo UPI = ProfileFrontend.GetUserProfile(remoteClient.AgentId);
            string notes = "";
            UPI.Notes.TryGetValue(new UUID(args[0]), out notes);
            if (notes == null || notes == "")
            {
                AvatarNotesUpdate(remoteClient, new UUID(args[0]), "");
                UPI = ProfileFrontend.GetUserProfile(remoteClient.AgentId);
                UPI.Notes.TryGetValue(new UUID(args[0]), out notes);
            }
            remoteClient.SendAvatarNotesReply(new UUID(args[0]), notes);
        }
        public void AvatarNotesUpdate(IClientAPI remoteClient, UUID queryTargetID, string queryNotes)
        {
            IUserProfileInfo UPI = ProfileFrontend.GetUserProfile(remoteClient.AgentId);
            string notes;
            if (queryNotes == "")
            {
                notes = "Insert your notes here.";
            }
            else
                notes = queryNotes;
            string oldNotes;
            if (UPI.Notes.TryGetValue(queryTargetID, out oldNotes))
                UPI.Notes.Remove(queryTargetID);
            
            UPI.Notes.Add(queryTargetID, notes);
            ProfileFrontend.UpdateUserNotes(remoteClient.AgentId, queryTargetID,notes, UPI);
        }
        public void AvatarInterestsUpdate(IClientAPI remoteClient, uint wantmask, string wanttext, uint skillsmask, string skillstext, string languages)
        {
            IUserProfileInfo UPI = ProfileFrontend.GetUserProfile(remoteClient.AgentId);
            UPI.Interests.WantToMask = wantmask.ToString();
            UPI.Interests.WantToText = wanttext;
            UPI.Interests.CanDoMask = skillsmask.ToString();
            UPI.Interests.CanDoText = skillstext;
            UPI.Interests.Languages = languages;
            ProfileFrontend.UpdateUserProfile(UPI);
        }

        public void RequestAvatarProperty(IClientAPI remoteClient, UUID target)
        {
            IUserProfileInfo UPI = ProfileFrontend.GetUserProfile(target);
            OpenSim.Services.Interfaces.GridUserInfo TargetPI = m_scene.GridUserService.GetGridUserInfo(target.ToString());
            UserAccount TargetAccount = m_scene.UserAccountService.GetUserAccount(UUID.Zero, target);
            bool isFriend = IsFriendOfUser(remoteClient.AgentId, target);
            if (isFriend)
            {
                uint agentOnline = 0;
                if (TargetPI.Online)
                {
                    agentOnline = 16;
                }
                SendProfile(remoteClient, UPI, target, agentOnline);
            }
            else
            {
                //See if all can see this person
                if (UPI.Visible)
                {
                    //Not a friend, so send the first page only and if they are online
                    uint agentOnline = 0;
                    if (TargetPI.Online)
                    {
                        agentOnline = 16;
                    }
                    
                    Byte[] charterMember;
                    if (UPI.MembershipGroup == "")
                    {
                        charterMember = new Byte[1];
                        charterMember[0] = (Byte)((TargetAccount.UserFlags & 0xf00) >> 8);
                    }
                    else
                    {
                        charterMember = OpenMetaverse.Utils.StringToBytes(UPI.MembershipGroup);
                    }
                    remoteClient.SendAvatarProperties(UPI.PrincipalID, "",
                                                      Util.ToDateTime(UPI.Created).ToString("M/d/yyyy", CultureInfo.InvariantCulture),
                                                      charterMember, "", (uint)(TargetAccount.UserFlags & agentOnline),
                                                      UUID.Zero, UUID.Zero, "", UUID.Zero);
                }
                else
                {
                    //Not a friend, so send the first page only.

                    Byte[] charterMember;
                    if (UPI.MembershipGroup == "")
                    {
                        charterMember = new Byte[1];
                        charterMember[0] = (Byte)((TargetAccount.UserFlags & 0xf00) >> 8);
                    }
                    else
                    {
                        charterMember = OpenMetaverse.Utils.StringToBytes(UPI.MembershipGroup);
                    }
                    remoteClient.SendAvatarProperties(UPI.PrincipalID, "",
                                                      Util.ToDateTime(UPI.Created).ToString("M/d/yyyy", CultureInfo.InvariantCulture),
                                                      charterMember, "", (uint)(TargetAccount.UserFlags),
                                                      UUID.Zero, UUID.Zero, "", UUID.Zero);
                }
            }
        }

        public void UpdateAvatarProperties(IClientAPI remoteClient, OpenSim.Framework.UserProfileData newProfile, bool allowpublish, bool maturepublish)
        {
            IUserProfileInfo UPI = ProfileFrontend.GetUserProfile(newProfile.ID);
            
            UPI.Image = newProfile.Image;
            UPI.FirstLifeImage = newProfile.FirstLifeImage;
            UPI.AboutText = newProfile.AboutText;
            UPI.FirstLifeAboutText = newProfile.FirstLifeAboutText;
            if (newProfile.ProfileUrl != "")
            {
                UPI.WebURL = newProfile.ProfileUrl;
            }

            UPI.AllowPublish = allowpublish;
            UPI.MaturePublish = maturepublish;
            ProfileFrontend.UpdateUserProfile(UPI);
            SendProfile(remoteClient, UPI, remoteClient.AgentId, 16);
        }

        private void SendProfile(IClientAPI remoteClient, IUserProfileInfo Profile, UUID target, uint agentOnline)
        {
            UserAccount account = m_scene.UserAccountService.GetUserAccount(UUID.Zero, target);
            Byte[] charterMember;
            if (Profile.MembershipGroup == " ")
            {
                charterMember = new Byte[1];
                charterMember[0] = (Byte)((account.UserFlags & 0xf00) >> 8);
            }
            else
            {
                charterMember = OpenMetaverse.Utils.StringToBytes(Profile.MembershipGroup);
            }
            uint membershipGroupINT = 0;
            if (Profile.MembershipGroup != "")
                membershipGroupINT = 4;

            uint flags = Convert.ToUInt32(Profile.AllowPublish) + Convert.ToUInt32(Profile.MaturePublish) + membershipGroupINT + (uint)agentOnline + (uint)account.UserFlags;
            remoteClient.SendAvatarInterestsReply(target, Convert.ToUInt32(Profile.Interests.WantToMask), Profile.Interests.WantToText, Convert.ToUInt32(Profile.Interests.CanDoMask), Profile.Interests.CanDoText, Profile.Interests.Languages);
            remoteClient.SendAvatarProperties(Profile.PrincipalID, Profile.AboutText,
                                              Util.ToDateTime(Profile.Created).ToString("M/d/yyyy", CultureInfo.InvariantCulture),
                                              charterMember, Profile.FirstLifeAboutText, flags,
                                              Profile.FirstLifeImage, Profile.Image, Profile.WebURL, new UUID(Profile.Partner));
        }
        
        public void UserPreferencesRequest(IClientAPI remoteClient)
        {
            IUserProfileInfo UPI = ProfileFrontend.GetUserProfile(remoteClient.AgentId);
            UserAccount account = m_scene.UserAccountService.GetUserAccount(UUID.Zero, remoteClient.AgentId);
            remoteClient.SendUserInfoReply(UPI.Visible, UPI.IMViaEmail, account.Email);
        }

        public void UpdateUserPreferences(bool imViaEmail, bool visible, IClientAPI remoteClient)
        {
            IUserProfileInfo UPI = ProfileFrontend.GetUserProfile(remoteClient.AgentId);
            UPI.Visible = visible;
            UPI.IMViaEmail = imViaEmail;
            ProfileFrontend.UpdateUserProfile(UPI);
        }
        #endregion

        #region Search Module
        
        protected void DirPlacesQuery(IClientAPI remoteClient, UUID queryID,
                                      string queryText, int queryFlags, int category, string simName,
                                      int queryStart)
        {
        	DirPlacesReplyData[] ReturnValues = ProfileData.PlacesQuery(queryText,category.ToString(),"searchparcels","PID,PName,PForSale,PAuction,PDwell",queryStart);

            DirPlacesReplyData[] data = new DirPlacesReplyData[10];

            int i = 0;
            foreach (DirPlacesReplyData d in ReturnValues)
            {
            	data[i] = d;
            	i++;
            	if(i == 10)
            	{
                	remoteClient.SendDirPlacesReply(queryID, data);
                    i = 0;
                    data = new DirPlacesReplyData[10];
            	}
            }
            remoteClient.SendDirPlacesReply(queryID, data);
        }

        public void DirPopularQuery(IClientAPI remoteClient, UUID queryID, uint queryFlags)
        {
        	/// <summary>
        	/// Decapriated.
        	/// </summary>
        }
        
        public void DirLandQuery(IClientAPI remoteClient, UUID queryID,
                                 uint queryFlags, uint searchType, int price, int area,
                                 int queryStart)
        {
			DirLandReplyData[] ReturnValues = ProfileData.LandForSaleQuery(searchType.ToString(),price.ToString(),area.ToString(),"searchparcelsales","PID,PName,PAuction,PSalePrice,PArea",queryStart);

            DirLandReplyData[] data = new DirLandReplyData[10];

            int i = 0;
            foreach (DirLandReplyData d in ReturnValues)
            {
            	data[i] = d;
            	i++;
            	if(i == 10)
            	{
                	remoteClient.SendDirLandReply(queryID, data);
                    i = 0;
                    data = new DirLandReplyData[10];
            	}
            }
            remoteClient.SendDirLandReply(queryID, data);
        }

        public void DirFindQuery(IClientAPI remoteClient, UUID queryID,
                                 string queryText, uint queryFlags, int queryStart)
        {
            if ((queryFlags & 1) != 0)
            {
                DirPeopleQuery(remoteClient, queryID, queryText, queryFlags,
                               queryStart);
                return;
            }
            else if ((queryFlags & 32) != 0)
            {
                DirEventsQuery(remoteClient, queryID, queryText, queryFlags,
                               queryStart);
                return;
            }
        }

        public void DirPeopleQuery(IClientAPI remoteClient, UUID queryID,
                                   string queryText, uint queryFlags, int queryStart)
        {
            List<UserAccount> accounts = m_Scenes[0].UserAccountService.GetUserAccounts(m_Scenes[0].RegionInfo.ScopeID, queryText);
			DirPeopleReplyData[] data =
                    new DirPeopleReplyData[accounts.Count];

            int i = 0;
            foreach (UserAccount item in accounts)
            {
                IUserProfileInfo UserProfile = ProfileFrontend.GetUserProfile(item.PrincipalID);
                if (UserProfile == null)
                {
                    data[i] = new DirPeopleReplyData();
                    data[i].agentID = item.PrincipalID;
                    data[i].firstName = item.FirstName;
                    data[i].lastName = item.LastName;
                    if (GroupsModule == null)
                        data[i].group = "";
                    else
                    {
                        data[i].group = "";
                        GroupMembershipData[] memberships = GroupsModule.GetMembershipData(item.PrincipalID);
                        foreach (GroupMembershipData membership in memberships)
                        {
                            if (membership.Active)
                                data[i].group = membership.GroupName;
                        }
                    }
                    OpenSim.Services.Interfaces.GridUserInfo Pinfo = m_scene.GridUserService.GetGridUserInfo(item.PrincipalID.ToString());
                    if(Pinfo != null)
                        data[i].online = true;
                    data[i].reputation = 0;
                    i++;
                    continue;
                }
                if (UserProfile.AllowPublish)
                {
                	data[i] = new DirPeopleReplyData();
                    data[i].agentID = item.PrincipalID;
                    data[i].firstName = item.FirstName;
                    data[i].lastName = item.LastName;
                    if(GroupsModule == null)
                    	data[i].group = "";
                    else
                    {
                    	data[i].group = "";
                    	GroupMembershipData[] memberships = GroupsModule.GetMembershipData(item.PrincipalID);
                    	foreach(GroupMembershipData membership in memberships)
                    	{
                    		if(membership.Active)
                    			data[i].group = membership.GroupName;
                    	}
                    }
                    OpenSim.Services.Interfaces.GridUserInfo Pinfo = m_scene.GridUserService.GetGridUserInfo(item.PrincipalID.ToString());
                    data[i].online = Pinfo.Online;
                    data[i].reputation = 0;
                    i++;
                }
            }

            remoteClient.SendDirPeopleReply(queryID, data);
        }

        public void DirEventsQuery(IClientAPI remoteClient, UUID queryID,
                                   string queryText, uint queryFlags, int queryStart)
        {
            DirEventsReplyData[] ReturnValues = ProfileData.EventQuery(queryText, queryFlags.ToString(),"events","EOwnerID,EName,EID,EDate,EFlags",queryStart);

            DirEventsReplyData[] data = new DirEventsReplyData[10];
			int i = 0;
			
            foreach (DirEventsReplyData d in ReturnValues)
            {
            	data[i] = d;
            	i++;
            	if(i == 10)
            	{
                	remoteClient.SendDirEventsReply(queryID, data);
                    i = 0;
                    data = new DirEventsReplyData[10];
            	}
            }
            remoteClient.SendDirEventsReply(queryID, data);
        }
		
        public void DirClassifiedQuery(IClientAPI remoteClient, UUID queryID,
                                       string queryText, uint queryFlags, uint category,
                                       int queryStart)
        {
        	DirClassifiedReplyData[] ReplyData = ProfileData.ClassifiedsQuery(queryText, category.ToString(), queryFlags.ToString(),queryStart);
            
        	DirClassifiedReplyData[] data = new DirClassifiedReplyData[10];
			int i = 0;
			
            foreach (DirClassifiedReplyData d in ReplyData)
            {
            	data[i] = d;
            	i++;
            	if(i == 10)
            	{
                	remoteClient.SendDirClassifiedReply(queryID, data);
                    i = 0;
                    data = new DirClassifiedReplyData[10];
            	}
            }
            remoteClient.SendDirClassifiedReply(queryID, data);
        }

        public void EventInfoRequest(IClientAPI remoteClient, uint queryEventID)
        {
            EventData data = new EventData();
            data = ProfileData.GetEventInfo(queryEventID.ToString());
            remoteClient.SendEventInfoReply(data);
        }

        public virtual void HandleMapItemRequest(IClientAPI remoteClient, uint flags,
                                                 uint EstateID, bool godlike, uint itemtype, ulong regionhandle)
        {
            //All the parts are in for this, except for popular places and those are not in as they are not reqested anymore.
            
            List<mapItemReply> mapitems = new List<mapItemReply>();
            mapItemReply mapitem = new mapItemReply();

            #region Telehub
            if (itemtype == (uint)OpenMetaverse.GridItemType.Telehub)
            {
                GridFrontend GF = new GridFrontend();

                /*int i = 0;
                List<string> TelehubsX = new List<string>();
                List<string> TelehubsY = new List<string>();
                List<string> RegionUUIDs = new List<string>();
                foreach (string info in Telehubs)
                {
                    if (i == 0)
                    {
                        if(info != "")
                            TelehubsX.Add(info);
                    }
                    if (i == 1)
                    {
                        if (info != "")
                            TelehubsY.Add(info);
                    }
                    if (i == 2)
                    {
                        if (info != "")
                            RegionUUIDs.Add(info);
                        i = -1;
                    }
                    i += 1;
                }
                int tc = Environment.TickCount;
                i = 0;
                if (TelehubsX.Count != 0)
                {
                    for (i = 0; i + 1 <= TelehubsX.Count; i++)
                    {
                        OpenSim.Services.Interfaces.GridRegion region = m_scene.GridService.GetRegionByUUID(UUID.Zero, new UUID(RegionUUIDs[i]));
                        mapitem = new mapItemReply();
                        mapitem.x = (uint)(region.RegionLocX + Convert.ToUInt32(TelehubsX[i]));
                        mapitem.y = (uint)(region.RegionLocY + Convert.ToUInt32(TelehubsY[i]));
                        mapitem.id = UUID.Zero;
                        mapitem.name = Util.Md5Hash(region.RegionName + tc.ToString());
                        mapitem.Extra = 1;
                        mapitem.Extra2 = 0;
                        mapitems.Add(mapitem);
                    }
                }
                if (mapitems.Count != 0)
                {
                    remoteClient.SendMapItemReply(mapitems.ToArray(), itemtype, flags);
                    mapitems.Clear();
                }*/
            }

			#endregion

            #region Land for sale

            if (itemtype == (uint)OpenMetaverse.GridItemType.LandForSale)
            {
                DirLandReplyData[] Landdata = ProfileData.LandForSaleQuery("4294967295",int.MaxValue.ToString(),"0","searchparcelsales","PID,PName,PAuction,PSalePrice,PArea",0);
                    
                uint locX = 0;
                uint locY = 0;
                foreach (DirLandReplyData landDir in Landdata)
                {
                    List<string> ParcelInfo = GenericData.Query("PID", landDir.parcelID.ToString(), "parcels", "PLandingX, PLandingY, PRegionID, PIsMature");
                    if (Convert.ToBoolean(ParcelInfo[3]))
                    {
                        continue;
                    }
                    foreach (Scene scene in m_Scenes)
                    {
                        if (scene.RegionInfo.RegionID == new UUID(ParcelInfo[2]))
                        {
                            locX = scene.RegionInfo.RegionLocX;
                            locY = scene.RegionInfo.RegionLocY;
                        }
                    }
                    mapitem = new mapItemReply();
                    mapitem.x = (uint)(locX + Convert.ToDecimal(ParcelInfo[0]));
                    mapitem.y = (uint)(locY + Convert.ToDecimal(ParcelInfo[1]));
                    mapitem.id = landDir.parcelID;
                    mapitem.name = landDir.name;
                    mapitem.Extra = landDir.actualArea;
                    mapitem.Extra2 = landDir.salePrice;
                    mapitems.Add(mapitem);
                }
                if (mapitems.Count != 0)
                {
                    remoteClient.SendMapItemReply(mapitems.ToArray(), itemtype, flags);
                    mapitems.Clear();
                }
            }

            if (itemtype == (uint)OpenMetaverse.GridItemType.AdultLandForSale)
            {
                DirLandReplyData[] Landdata = ProfileData.LandForSaleQuery("4294967295", int.MaxValue.ToString(), "0", "searchparcelsales", "PID,PName,PAuction,PSalePrice,PArea", 0);

                uint locX = 0;
                uint locY = 0;
                foreach (DirLandReplyData landDir in Landdata)
                {
                    List<string> ParcelInfo = GenericData.Query("PID", landDir.parcelID.ToString(), "parcels", "PLandingX, PLandingY, PRegionID, PIsMature");
                    if (!Convert.ToBoolean(ParcelInfo[3]))
                    {
                        continue;
                    }
                    foreach (Scene scene in m_Scenes)
                    {
                        if (scene.RegionInfo.RegionID == new UUID(ParcelInfo[2]))
                        {
                            locX = scene.RegionInfo.RegionLocX;
                            locY = scene.RegionInfo.RegionLocY;
                        }
                    }
                    mapitem = new mapItemReply();
                    mapitem.x = (uint)(locX + Convert.ToDecimal(ParcelInfo[0]));
                    mapitem.y = (uint)(locY + Convert.ToDecimal(ParcelInfo[1]));
                    mapitem.id = landDir.parcelID;
                    mapitem.name = landDir.name;
                    mapitem.Extra = landDir.actualArea;
                    mapitem.Extra2 = landDir.salePrice;
                    mapitems.Add(mapitem);
                }
                if (mapitems.Count != 0)
                {
                    remoteClient.SendMapItemReply(mapitems.ToArray(), itemtype, flags);
                    mapitems.Clear();
                }
            }

            #endregion

            #region Events 
            
            if (itemtype == (uint)OpenMetaverse.GridItemType.PgEvent)
            {
            	DirEventsReplyData[] Eventdata = ProfileData.GetAllEventsNearXY("events",0,0);
                foreach (DirEventsReplyData eventData in Eventdata)
                {
                	List<string> query = GenericData.Query("EID", eventData.eventID.ToString(), "events", "EGlobalPos, ESimName, EMature");
                    
                    string RegionName = query[1];
                    string globalPos = query[0];
                    bool Mature = Convert.ToBoolean(query[2]);
                    if (Mature)
                        continue;
                    OpenSim.Services.Interfaces.GridRegion region = m_scene.GridService.GetRegionByName(UUID.Zero, RegionName);
                    string[] Position = globalPos.Split(',');
                    mapitem = new mapItemReply();
                    mapitem.x = (uint)(region.RegionLocX + Convert.ToUInt32(Position[0]));
                    mapitem.y = (uint)(region.RegionLocY + Convert.ToUInt32(Position[1]));
                    mapitem.id = eventData.ownerID;
                    mapitem.name = eventData.name;
                    mapitem.Extra2 = (int)DirectoryManager.EventFlags.PG;
                    mapitems.Add(mapitem);
                }
                if (mapitems.Count != 0)
                {
                    remoteClient.SendMapItemReply(mapitems.ToArray(), itemtype, flags);
                    mapitems.Clear();
                }
            }

            if (itemtype == (uint)OpenMetaverse.GridItemType.AdultEvent)
            {
                DirEventsReplyData[] Eventdata = ProfileData.GetAllEventsNearXY("events", 0, 0);
                foreach (DirEventsReplyData eventData in Eventdata)
                {
                    List<string> query = GenericData.Query("EID", eventData.eventID.ToString(), "events", "EGlobalPos, ESimName, EMature");
                    string RegionName = query[1];
                    string globalPos = query[0];
                    bool Mature = Convert.ToBoolean(query[2]);
                    if (!Mature)
                        continue;
                    OpenSim.Services.Interfaces.GridRegion region = m_scene.GridService.GetRegionByName(UUID.Zero, RegionName);
                    string[] Position = globalPos.Split(',');
                    mapitem = new mapItemReply();
                    mapitem.x = (uint)(region.RegionLocX + Convert.ToUInt32(Position[0]));
                    mapitem.y = (uint)(region.RegionLocY + Convert.ToUInt32(Position[1]));
                    mapitem.id = eventData.ownerID;
                    mapitem.name = eventData.name;
                    mapitem.Extra2 = (int)DirectoryManager.EventFlags.Adult;
                    mapitems.Add(mapitem);
                }
                if (mapitems.Count != 0)
                {
                    remoteClient.SendMapItemReply(mapitems.ToArray(), itemtype, flags);
                    mapitems.Clear();
                }
            }
            if (itemtype == (uint)OpenMetaverse.GridItemType.MatureEvent)
            {
                DirEventsReplyData[] Eventdata = ProfileData.GetAllEventsNearXY("events", 0, 0);
                foreach (DirEventsReplyData eventData in Eventdata)
                {
                    List<string> query = GenericData.Query("EID", eventData.eventID.ToString(), "events", "EGlobalPos, ESimName, EMature");
                    string RegionName = query[1];
                    string globalPos = query[0];
                    bool Mature = Convert.ToBoolean(query[2]);
                    if (!Mature)
                        continue;
                    OpenSim.Services.Interfaces.GridRegion region = m_scene.GridService.GetRegionByName(UUID.Zero, RegionName);
                    string[] Position = globalPos.Split(',');
                    mapitem = new mapItemReply();
                    mapitem.x = (uint)(region.RegionLocX + Convert.ToUInt32(Position[0]));
                    mapitem.y = (uint)(region.RegionLocY + Convert.ToUInt32(Position[1]));
                    mapitem.id = eventData.ownerID;
                    mapitem.name = eventData.name;
                    mapitem.Extra2 = (int)DirectoryManager.EventFlags.Mature;
                    mapitems.Add(mapitem);
                }
                if (mapitems.Count != 0)
                {
                    remoteClient.SendMapItemReply(mapitems.ToArray(), itemtype, flags);
                    mapitems.Clear();
                }
            }

            #endregion

            #region Classified

            if (itemtype == (uint)OpenMetaverse.GridItemType.Classified)
            {
                Classified[] Classifieds = ProfileData.GetClassifieds();
                foreach (Classified classified in Classifieds)
                {
                    Vector3 Position = new Vector3();
                    Vector3.TryParse(classified.PosGlobal, out Position);
                    OpenSim.Services.Interfaces.GridRegion region = m_scene.GridService.GetRegionByName(UUID.Zero, classified.SimName);
                    mapitem = new mapItemReply();
                    mapitem.x = (uint)(region.RegionLocX + Convert.ToUInt32(Position.X));
                    mapitem.y = (uint)(region.RegionLocY + Convert.ToUInt32(Position.Y));
                    mapitem.id = new UUID(classified.CreatorUUID);
                    mapitem.name = classified.Name;
                    mapitem.Extra = 0;
                    mapitem.Extra2 = 0;
                    mapitems.Add(mapitem);
                }
                if (mapitems.Count != 0)
                {
                    remoteClient.SendMapItemReply(mapitems.ToArray(), itemtype, flags);
                    mapitems.Clear();
                }
            }

            #endregion
        }

        public void OnPlacesQueryRequest(UUID QueryID, UUID TransactionID, string QueryText, uint QueryFlags, byte Category, string SimName, IClientAPI client)
        {
            if (QueryFlags == 64) //Agent Owned
            {
                List<ILandObject> LandQueried = new List<ILandObject>();
                List<string> SimNames = new List<string>();
                List<string> SimXs = new List<string>();
                List<string> SimYs = new List<string>();
                List<OpenSim.Framework.RegionInfo> RegionInfos = new List<OpenSim.Framework.RegionInfo>();
                List<object> Parcels = null;
                foreach (Scene scene in m_Scenes)
                {
                    List<ILandObject> AllParcels = scene.LandChannel.AllParcels();
                    foreach (ILandObject LandObject in AllParcels)
                    {
                        if (LandObject.LandData.OwnerID == client.AgentId)
                        {
                            SimNames.Add(scene.RegionInfo.RegionName);
                            if (LandObject.LandData.UserLocation == Vector3.Zero)
                            {
                                for (int x = 0; x < 64; x++)
                                {
                                    for (int y = 0; y < 64; y++)
                                    {
                                        if (LandObject.LandBitmap[x, y])
                                        {
                                            SimXs.Add(((x * 4)+(scene.RegionInfo.RegionLocX * 256)).ToString());
                                            SimYs.Add(((y * 4)+(scene.RegionInfo.RegionLocY * 256)).ToString());
                                            RegionInfos.Add(scene.RegionInfo);
                                            x = (int)Constants.RegionSize;
                                            y = (int)Constants.RegionSize;
                                            continue;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                SimXs.Add(((LandObject.LandData.UserLocation.X) + (scene.RegionInfo.RegionLocX * 256)).ToString());
                                SimYs.Add(((LandObject.LandData.UserLocation.Y) + (scene.RegionInfo.RegionLocY * 256)).ToString());
                            }
                            LandQueried.Add(LandObject);
                        }
                    }
                }
                Parcels = new List<object>(LandQueried.ToArray());
                client.SendPlacesQuery(SimNames, Parcels, QueryID, client.AgentId, TransactionID, SimXs, SimYs, RegionInfos.ToArray());
            }
        }

        private void StartSearch()
        {
        	aTimer = new System.Timers.Timer(parserTime);
        	aTimer.Elapsed += new System.Timers.ElapsedEventHandler(ParseRegions);
        	aTimer.Enabled = true;
        	aTimer.Start();
        	foreach (Scene scene in m_Scenes)
        	{
        		FireParser(scene, scene.RegionInfo.RegionName);
        	}
        }

        private void ParseRegions(object source, System.Timers.ElapsedEventArgs e)
        {
        	foreach (Scene scene in m_Scenes)
        	{
        		FireParser(scene, scene.RegionInfo.RegionName);
        	}
        }

        #region XML Info Classes

        private class RegionXMLInfo
        {
        	public string UUID;
        	public string Name;
        	public string Handle;
        	public string URL;
        	public string UserName;
        	public string UserUUID;
        }

        private class ObjectXMLInfo
        {
        	public string UUID;
        	public string RegionUUID;
        	public string ParcelUUID;
        	public string Title;
        	public string Desc;
        	public string Flags;
        }

        private class ParcelXMLInfo
        {
        	public string Name;
        	public string UUID;
        	public string InfoUUID;
        	public string Landing;
        	public string Desc;
        	public string Area;
        	public string Category;
        	public string SalePrice;
        	public string Dwell;
        	public string OwnerUUID;
        	public string GroupUUID;
        	public string ForSale;
        	public string Directory;
        	public string Build;
        	public string Script;
        	public string Public;
        }

        #endregion

        #region Parser

        private void FireParser(Scene currentScene, string regionName)
        {
        	m_log.Info("[SearchModule]: Starting Search for region "+regionName+".");
        	XmlDocument doc = DataSnapShotManager.GetSnapshot(regionName);
        	if(doc == null)
        	{
        		m_log.Error("[SearchModule]: Null ref in the XMLDOC.");
        		return;
        	}
        	XmlNodeList rootL = doc.GetElementsByTagName("region");
        	RegionXMLInfo info = new RegionXMLInfo();
        	foreach(XmlNode rootNode in rootL)
        	{
        		foreach(XmlNode subRootNode in rootNode.ChildNodes)
        		{
        			if(subRootNode.Name == "info")
        			{
        				foreach (XmlNode part in subRootNode.ChildNodes)
        				{
        					switch (part.Name)
        					{
        						case "uuid":
        							info.UUID = part.InnerText;
        							break;
        						case "name":
        							info.Name = part.InnerText;
        							break;
        						case "handle":
        							info.Handle = part.InnerText;
        							break;
        						case "url":
        							info.URL = part.InnerText;
        							break;
        					}
        				}
        				
        				List<string> query = GenericData.Query("RID", info.UUID.ToString(), "searchregions", "*");
                        if (query.Count != 0)
                        {
                            if (query[0] != "")
                            {
                                GenericData.Delete("searchregions", new string[] { "RID" }, new string[] { info.UUID.ToString() });
                                GenericData.Delete("searchparcels", new string[] { "RID" }, new string[] { info.UUID.ToString() });
                                GenericData.Delete("searchobjects", new string[] { "RID" }, new string[] { info.UUID.ToString() });
                                GenericData.Delete("searchallparcels", new string[] { "RID" }, new string[] { info.UUID.ToString() });
                                GenericData.Delete("searchparcelsales", new string[] { "RID" }, new string[] { info.UUID.ToString() });
                            }
                        }
        			}
        		}
        	}
        	
        	
        	foreach(XmlNode rootNode in rootL)
        	{
        		foreach(XmlNode subRootNode in rootNode.ChildNodes)
        		{
        			if(subRootNode.Name == "data")
        			{
        				foreach (XmlNode part in subRootNode.ChildNodes)
        				{
        					if(part.Name == "estate")
        					{
        						foreach (XmlNode subpart in part.ChildNodes)
        						{
        							foreach (XmlNode subsubpart in subpart.ChildNodes)
        							{
        								switch (subsubpart.Name)
        								{
        									case "uuid":
        										info.UserUUID = subsubpart.InnerText;
        										break;
        									case "name":
        										info.UserName = subsubpart.InnerText;
        										break;
        								}
        							}
        						}
        						GenericData.Insert("searchregions", new string[] { info.Name, info.UUID, info.Handle, info.URL, info.UserName, info.UserName });
        					}
        					if(part.Name == "objectdata")
        					{
        						foreach (XmlNode subsubpart in part.ChildNodes)
        						{
        							ObjectXMLInfo OInfo = new ObjectXMLInfo();
        							foreach (XmlNode subpart in subsubpart.ChildNodes)
        							{
        								switch (subpart.Name)
        								{
        									case "uuid":
        										OInfo.UUID = subpart.InnerText;
        										break;
        									case "regionuuid":
        										OInfo.RegionUUID = subpart.InnerText;
        										break;
        									case "parceluuid":
        										OInfo.ParcelUUID = subpart.InnerText;
        										break;
        									case "title":
        										OInfo.Title = subpart.InnerText;
        										break;
        									case "description":
        										OInfo.Desc = subpart.InnerText;
        										break;
        									case "flags":
        										OInfo.Flags = subpart.InnerText;
        										break;
        								}
        							}
        							if(OInfo.UUID != null)
        								GenericData.Insert("searchobjects", new string[] { OInfo.UUID, OInfo.ParcelUUID, OInfo.Title, OInfo.Desc, OInfo.RegionUUID });
        						}
        					}
        					if(part.Name == "parceldata")
        					{
        						foreach (XmlNode pppart in part.ChildNodes)
        						{
        							ParcelXMLInfo PInfo = new ParcelXMLInfo();
                                    if (pppart.Attributes != null)
                                    {
                                        foreach (XmlNode att in pppart.Attributes)
                                        {
                                            switch (att.Name)
                                            {
                                                case "build":
                                                    PInfo.Build = att.InnerText;
                                                    break;
                                                case "category":
                                                    PInfo.Category = att.InnerText;
                                                    break;
                                                case "showinsearch":
                                                    PInfo.Directory = att.InnerText;
                                                    break;
                                                case "forsale":
                                                    PInfo.ForSale = att.InnerText;
                                                    break;
                                                case "public":
                                                    PInfo.Public = att.InnerText;
                                                    break;
                                                case "salesprice":
                                                    PInfo.SalePrice = att.InnerText;
                                                    break;
                                                case "scripts":
                                                    PInfo.Script = att.InnerText;
                                                    break;
                                            }
                                        }
                                    }
        							foreach (XmlNode ppart in pppart.ChildNodes)
        							{							
        								switch (ppart.Name)
        								{
        									case "area":
        										PInfo.Area = ppart.InnerText;
        										break;
        									case "description":
        										PInfo.Desc = ppart.InnerText;
        										break;
        									case "dwell":
        										PInfo.Dwell = ppart.InnerText;
        										break;
        									case "groupuuid":
        										PInfo.GroupUUID = ppart.ChildNodes[0].InnerText;
        										break;
        									case "infouuid":
        										PInfo.InfoUUID = ppart.InnerText;
        										break;
        									case "location":
        										PInfo.Landing = ppart.InnerText;
        										break;
        									case "name":
        										PInfo.Name = ppart.InnerText;
        										break;
        									case "owner":
        										PInfo.OwnerUUID = ppart.ChildNodes.Item(0).InnerText;
        										break;
        									case "uuid":
        										PInfo.UUID = ppart.InnerText;
        										break;
        								}
        							}
        							if(PInfo.UUID == null)
        								continue;
        							if(PInfo.GroupUUID == null)
        								PInfo.GroupUUID = UUID.Zero.ToString();
        							GenericData.Insert("searchallparcels", new string[] { info.UUID, PInfo.Name, PInfo.OwnerUUID, PInfo.GroupUUID, PInfo.Landing, PInfo.UUID, PInfo.InfoUUID, PInfo.Area });
        							if (Convert.ToBoolean(PInfo.Directory))
        								GenericData.Insert("searchparcels", new string[] { info.UUID, PInfo.Name, PInfo.UUID, PInfo.Landing, PInfo.Desc, PInfo.Category, PInfo.Build, PInfo.Script, PInfo.Public, PInfo.Dwell, PInfo.InfoUUID, false.ToString(), false.ToString() });
                                    GridRegionFlags flags = new Aurora.DataManager.Frontends.GridFrontend().GetRegionFlags(currentScene.RegionInfo.RegionID);
                                    if (Convert.ToBoolean(PInfo.ForSale))
        							{
                                        GenericData.Insert("searchparcelsales", new string[] { info.UUID, PInfo.Name, PInfo.UUID, PInfo.Area, PInfo.SalePrice, PInfo.Landing, PInfo.InfoUUID, PInfo.Dwell, currentScene.RegionInfo.EstateSettings.EstateID.ToString(), ((flags & GridRegionFlags.Hidden) == GridRegionFlags.Hidden).ToString() });
        							}
        						}
        					}
        				}
        			}
        		}
        	}
        }
        #endregion

        #endregion
    }
}

using System.Drawing;
﻿using C = ClientPackets;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirNetwork;
using S = ServerPackets;
using System.Text.RegularExpressions;
using Timer = Server.MirEnvir.Timer;
using Server.MirObjects.Monsters;
using System.Threading;

namespace Server.MirObjects
{
    public class PlayerObject : HumanObject
    {
        private long NextTradeTime;
        private long NextGroupInviteTime;

        public string GMPassword = Settings.GMPassword;
        public bool GMLogin, EnableGroupRecall, EnableGuildInvite, AllowMarriage, AllowLoverRecall, AllowMentor, HasMapShout, HasServerShout; //TODO - Remove        

        public long LastRecallTime, LastTeleportTime, LastProbeTime;
        public long NextMailTime;
        public long MenteeEXP;        

        public bool WarZone = false;

        public int CurrentHeroIndex;
        private HeroInfo currentHero;
        public HeroInfo CurrentHero
        {
            get { return currentHero; }
            set
            {
                currentHero = value;

                if (currentHero != null)
                {
                    Info.CurrentHeroIndex = currentHero.Index;
                    for (int i = 0; i < Info.Heroes.Length; i++)
                    {
                        if (Info.Heroes[i].Index != currentHero.Index) continue;
                        CurrentHeroIndex = i;
                        break;
                    }
                }
                else
                {
                    Info.CurrentHeroIndex = 0;
                    CurrentHeroIndex = -1;
                }
                
            }
        }
        public HeroObject Hero;

        protected AccountInfo account;
        public virtual AccountInfo Account
        {
            get { return account; }
            set { account = value; }
        }       
        
        public override bool CanMove
        {
            get
            {
                return base.CanMove && !Fishing;
            }
        }
        public override bool CanWalk
        {
            get
            {
                return base.CanMove && !Fishing;
            }
        }
        public override bool CanAttack
        {
            get
            {
                return base.CanAttack && !Fishing;
            }
        }
        protected override bool CanCast
        {
            get
            {
                return base.CanCast && !Fishing;
            }
        }

        public bool NewMail = false;

        public override int PKPoints
        {
            get { return Info.PKPoints; }
            set { Info.PKPoints = value; }
        }        

        public int BindMapIndex
        {
            get { return Info.BindMapIndex; }
            set { Info.BindMapIndex = value; }
        }
        public Point BindLocation
        {
            get { return Info.BindLocation; }
            set { Info.BindLocation = value; }
        }        

        public int FishingChance, FishingChanceCounter, FishingProgressMax, FishingProgress, FishingAutoReelChance = 0, FishingNibbleChance = 0;
        public bool Fishing, FishingAutocast, FishFound, FishFirstFound;

        public const long TurnDelay = 350, HarvestDelay = 350, FishingCastDelay = 750, FishingDelay = 200, MovementDelay = 2000;
        public long ChatTime, ShoutTime, FishingTime, FishingFoundTime, CreatureTimeLeftTicker, RestedTime, MovementTime;

        public byte ChatTick; 

        public bool SendIntelligentCreatureUpdates = false;        

        protected int _fishCounter, _restedCounter;

        public uint NPCObjectID;
        public int NPCScriptID;
        public NPCPage NPCPage;
        public Dictionary<NPCSegment, bool> NPCSuccess = new Dictionary<NPCSegment, bool>();
        public bool NPCDelayed;
        public List<string> NPCSpeech = new List<string>();
        public Dictionary<string, object> NPCData = new Dictionary<string, object>();

        public bool UserMatch;
        public string MatchName;
        public ItemType MatchType;
        public MarketPanelType MarketPanelType;
        public short MinShapes, MaxShapes;

        public int PageSent;
        public List<AuctionInfo> Search = new List<AuctionInfo>();        

        public bool CanCreateGuild = false;        
        
        public GuildObject PendingGuildInvite = null;
        public bool GuildNoticeChanged = true; //set to false first time client requests notice list, set to true each time someone in guild edits notice
        public bool GuildMembersChanged = true;//same as above but for members
        public bool GuildCanRequestItems = true;
        public bool RequestedGuildBuffInfo = false;

        public bool CanCreateHero = false;        
        public bool AllowGroup
        {
            get { return Info.AllowGroup; }
            set { Info.AllowGroup = value; }
        }

        public bool AllowTrade
        {
            get { return Info.AllowTrade; }
            set { Info.AllowTrade = value; }
        }

        public bool AllowObserve
        {
            get { return Info.AllowObserve; }
            set { Info.AllowObserve = value; }
        }

        public PlayerObject MarriageProposal;
        public PlayerObject DivorceProposal;
        public PlayerObject MentorRequest;

        public PlayerObject GroupInvitation;
        public PlayerObject TradeInvitation;

        public PlayerObject TradePartner = null;
        public bool TradeLocked = false;
        public uint TradeGoldAmount = 0;

        public PlayerObject ItemRentalPartner = null;
        public UserItem ItemRentalDepositedItem = null;
        public uint ItemRentalFeeAmount = 0;
        public uint ItemRentalPeriodLength = 0;
        public bool ItemRentalFeeLocked = false;
        public bool ItemRentalItemLocked = false;

        private long LastRankUpdate = Envir.Time;

        public List<QuestProgressInfo> CurrentQuests
        {
            get { return Info.CurrentQuests; }
        }

        public List<int> CompletedQuests
        {
            get { return Info.CompletedQuests; }
        }

        public PlayerObject() { }

        public PlayerObject(CharacterInfo info, MirConnection connection) : base(info, connection) { }
        protected override void Load(CharacterInfo info, MirConnection connection)
        {
            if (info.Player != null)
            {
                throw new InvalidOperationException("Player.Info 不能为空");
            }

            info.Player = this;
            info.Mount = new MountInfo(this);

            Connection = connection;
            Info = info;
            Account = Connection.Account;

            Stats = new Stats();

            Report = new Reporting(this);

            if (Account.AdminAccount)
            {
                IsGM = true;
                MessageQueue.Enqueue(string.Format("{0} 以游戏管理员身份登录", Name));
            }

            if (Level == 0) NewCharacter();

            if (Info.GuildIndex != -1)
            {
                MyGuild = Envir.GetGuild(Info.GuildIndex);
            }

            if (info.CurrentHeroIndex > 0)
                CurrentHero = Envir.GetHeroInfo(info.CurrentHeroIndex);

            RefreshStats();

            if (HP == 0)
            {
                SetHP(Stats[Stat.HP]);
                SetMP(Stats[Stat.MP]);

                CurrentLocation = BindLocation;
                CurrentMapIndex = BindMapIndex;

                if (Info.PKPoints >= 200)
                {
                    Map temp = Envir.GetMapByNameAndInstance(Settings.PKTownMapName, 1);
                    Point tempLocation = new Point(Settings.PKTownPositionX, Settings.PKTownPositionY);

                    if (temp != null && temp.ValidPoint(tempLocation))
                    {
                        CurrentMapIndex = temp.Info.Index;
                        CurrentLocation = tempLocation;
                    }
                }
            }

            Info.LastLoginDate = Envir.Now;
        }

        public void StopGame(byte reason)
        {
            if (Node == null) return;

            for (int i = Pets.Count - 1; i >= 0; i--)
            {
                MonsterObject pet = Pets[i];

                if (pet.Race == ObjectType.Creature)
                {
                    //dont save Creatures they will miss alot of AI-Info when they get spawned on login
                    UnSummonIntelligentCreature(((IntelligentCreatureObject)pet).PetType, false);

                    Pets.RemoveAt(i);
                    continue;
                }

                pet.Master = null;

                if (!pet.Dead)
                {
                    switch (Settings.PetSave)
                    {
                        case true when Settings.PetSave is true:

                            switch (Class)
                            {
                                case (MirClass.刺客):

                                    if (Info.Name != Settings.AssassinCloneName)
                                    {
                                        Info.Pets.Add(new PetInfo(pet));
                                    }

                                    break;
                                default:

                                    Info.Pets.Add(new PetInfo(pet));

                                    break;
                            }

                            break;
                        case false when Settings.PetSave is false:

                            switch (Class)
                            {
                                case (MirClass.法师):

                                    if (pet.Name == Settings.CloneName)
                                    {
                                        Info.Pets.Add(new PetInfo(pet));
                                    }
                                    else
                                    {
                                        Info.Pets.Add(new PetInfo(pet)
                                        {
                                            TameTime = pet.TameTime - Envir.Time
                                        });
                                    }

                                    break;
                            }

                            break;
                    }

                    Envir.MonsterCount--;
                    pet.CurrentMap.MonsterCount--;

                    pet.CurrentMap.RemoveObject(pet);
                    pet.Despawn();

                    Pets.RemoveAt(i);
                }
            }

            if (HeroSpawned)
                DespawnHero();
            
            for (int i = 0; i < Info.Magics.Count; i++)
            {
                var magic = Info.Magics[i];

                if (Envir.Time < (magic.CastTime + magic.GetDelay()))
                {
                    magic.CastTime -= Envir.Time;
                }
                else
                {
                    magic.CastTime = int.MinValue;
                }
            }

            for (int i = Buffs.Count - 1; i >= 0; i--)
            {
                var buff = Buffs[i];
                buff.Caster = null;
                buff.ObjectID = 0;

                if (buff.Properties.HasFlag(BuffProperty.RemoveOnExit))
                {
                    Buffs.RemoveAt(i);
                }
            }

            for (int i = 0; i < PoisonList.Count; i++)
            {
                var poison = PoisonList[i];
                poison.Owner = null;
            }

            if (MyGuild != null)
            {
                MyGuild.PlayerLogged(this, false);
            }

            Envir.Players.Remove(this);
            CurrentMap.RemoveObject(this);

            Despawn();
            LeaveGroup();
            TradeCancel();
            CancelItemRental();
            RefineCancel();
            LogoutRelationship();
            LogoutMentor();

            string logReason = LogOutReason(reason);

            MessageQueue.Enqueue(logReason);

            Fishing = false;

            Info.LastIP = Connection.IPAddress;
            Info.LastLogoutDate = Envir.Now;

            Report.Disconnected(logReason);
            Connection.WorldMapSetupSent = false;

            if (!IsGM)
            {
                Envir.OnlineRankingCount[0]--;
                Envir.OnlineRankingCount[(int)Class + 1]--;
            }

            CleanUp();
        }
        private string LogOutReason(byte reason)
        {
            switch (reason)
            {
                //0-10 are 'senddisconnect to client'
                case 0:
                    return string.Format("{0} 已退出游戏 原因: 服务器关闭", Name);
                case 1:
                    return string.Format("{0} 已退出游戏 原因: 再次登录", Name);
                case 2:
                    return string.Format("{0} 已退出游戏 原因: 聊天信息太长", Name);
                case 3:
                    return string.Format("{0} 已退出游戏 原因: 服务器崩溃", Name);
                case 4:
                    return string.Format("{0} 已退出游戏 原因: 被管理员踢出", Name);
                case 5:
                    return string.Format("{0} 已退出游戏 原因: 服务器人数已满", Name);
                case 6:
                    return string.Format("{0} 已注销 原因：账号已被封禁！", Name);
                case 10:
                    return string.Format("{0} 已退出游戏 原因: 客户端版本错误", Name);
                case 20:
                    return string.Format("{0} 已退出游戏 原因: 用户掉线或断开连接", Name);
                case 21:
                    return string.Format("{0} 已退出游戏 原因: 连接超时", Name);
                case 22:
                    return string.Format("{0} 已退出游戏 原因: 用户关闭游戏", Name);
                case 23:
                    return string.Format("{0} 已退出游戏 原因: 返回人物选择界面", Name);
                case 24:
                    return string.Format("{0} 已注销 原因：开始观察", Name);
                default:
                    return string.Format("{0} 已退出游戏 原因: 未知", Name);
            }
        }
        protected override void NewCharacter()
        {
            if (Envir.StartPoints.Count == 0) return;

            SetBind();

            base.NewCharacter();
        }
        public override void Process()
        {
            if (Connection == null || Node == null || Info == null) return;

            if (GroupInvitation != null && GroupInvitation.Node == null)
                GroupInvitation = null;

            base.Process();

            if (Settings.RestedPeriod > 0 && Envir.Time > RestedTime)
            {
                _restedCounter = InSafeZone ? _restedCounter + 1 : _restedCounter;

                if (_restedCounter > 0)
                {
                    int count = _restedCounter / (Settings.RestedPeriod * 60);

                    GiveRestedBonus(count);
                }

                RestedTime = Envir.Time + Settings.Second;
            }

            if (NewMail)
            {
                ReceiveChat(GameLanguage.NewMail, ChatType.System);

                GetMail();
            }

            if (Account.HasExpandedStorage && Envir.Now > Account.ExpandedStorageExpiryDate)
            {
                Account.HasExpandedStorage = false;
                ReceiveChat("扩展仓库已过期", ChatType.System);
                Enqueue(new S.ResizeStorage { Size = Account.Storage.Length, HasExpandedStorage = Account.HasExpandedStorage, ExpiryTime = Account.ExpandedStorageExpiryDate });
            }

            if (Fishing && Envir.Time > FishingTime)
            {
                _fishCounter++;
                UpdateFish();
            }

            RefreshCreaturesTimeLeft();
        }
        public override void Process(DelayedAction action)
        {
            if (action.FlaggedToRemove)
                return;

            switch (action.Type)
            {
                case DelayedType.Magic:
                    CompleteMagic(action.Params);
                    break;
                case DelayedType.Damage:
                    CompleteAttack(action.Params);
                    break;
                case DelayedType.MapMovement:
                    CompleteMapMovement(action.Params);
                    break;
                case DelayedType.Mine:
                    CompleteMine(action.Params);
                    break;
                case DelayedType.NPC:
                    CompleteNPC(action.Params);
                    break;
                case DelayedType.Poison:
                    CompletePoison(action.Params);
                    break;
                case DelayedType.DamageIndicator:
                    CompleteDamageIndicator(action.Params);
                    break;
                case DelayedType.Quest:
                    CompleteQuest(action.Params);
                    break;
                case DelayedType.SpellEffect:
                    CompleteSpellEffect(action.Params);
                    break;
            }
        }
        protected override void Moved()
        {
            base.Moved();
            CheckConquest();
            if (TradePartner != null)
                TradeCancel();

            if (ItemRentalPartner != null)
                CancelItemRental();
        }
        public override void Die()
        {
            if (SpecialMode.HasFlag(SpecialItemMode.Revival) && Envir.Time > LastRevivalTime)
            {
                LastRevivalTime = Envir.Time + 300000;

                for (var i = (int)EquipmentSlot.左戒指; i <= (int)EquipmentSlot.右戒指; i++)
                {
                    var item = Info.Equipment[i];

                    if (item == null) continue;
                    if (!(item.Info.Unique.HasFlag(SpecialItemMode.Revival)) || item.CurrentDura < 1000) continue;
                    SetHP(Stats[Stat.HP]);
                    item.CurrentDura = (ushort)(item.CurrentDura - 1000);
                    Enqueue(new S.DuraChanged { UniqueID = item.UniqueID, CurrentDura = item.CurrentDura });
                    RefreshStats();
                    ReceiveChat("你得到了重生的机会", ChatType.System);
                    return;
                }
            }

            if (LastHitter != null && LastHitter.Race == ObjectType.Player)
            {
                PlayerObject hitter = (PlayerObject)LastHitter;

                if (AtWar(hitter) || WarZone)
                {
                    hitter.ReceiveChat(string.Format("你受到了正义的庇护"), ChatType.System);
                }
                else if (Envir.Time > BrownTime && PKPoints < 200)
                {
                    UserItem weapon = hitter.Info.Equipment[(byte)EquipmentSlot.武器];

                    hitter.PKPoints = Math.Min(int.MaxValue, LastHitter.PKPoints + 100);
                    hitter.ReceiveChat(string.Format("你杀了 {0}", Name), ChatType.System);
                    ReceiveChat(string.Format("你被 {0} 杀害了", LastHitter.Name), ChatType.System);

                    if (weapon != null && weapon.AddedStats[Stat.幸运] > (Settings.MaxLuck * -1) && Envir.Random.Next(4) == 0)
                    {
                        weapon.AddedStats[Stat.幸运]--;
                        hitter.ReceiveChat("武器受到了诅咒", ChatType.System);
                        hitter.Enqueue(new S.RefreshItem { Item = weapon });
                    }
                }
            }

            UnSummonIntelligentCreature(SummonedCreatureType);

            if (HeroSpawned)
                DespawnHero();

            for (int i = Pets.Count - 1; i >= 0; i--)
            {
                if (Pets[i].Dead) continue;
                Pets[i].Die();
            }

            if (HasHero && HeroSpawned && !Hero.Dead)
            {
                Hero.Die();
            }

            RemoveBuff(BuffType.魔法盾);
            RemoveBuff(BuffType.金刚术);

            if (PKPoints > 200)
                RedDeathDrop(LastHitter);
            else if (!InSafeZone)
                DeathDrop(LastHitter);

            HP = 0;
            Dead = true;

            LogTime = Envir.Time;
            BrownTime = Envir.Time;

            Enqueue(new S.Death { Direction = Direction, Location = CurrentLocation });
            Broadcast(new S.ObjectDied { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation });

            for (int i = 0; i < Buffs.Count; i++)
            {
                Buff buff = Buffs[i];

                if (!buff.Properties.HasFlag(BuffProperty.RemoveOnDeath)) continue;

                RemoveBuff(buff.Type);
            }

            PoisonList.Clear();
            InTrapRock = false;

            CallDefaultNPC(DefaultNPCType.Die);

            Report.Died(CurrentMap.Info.FileName);
        }
        private void RedDeathDrop(MapObject killer)
        {
            if (killer == null || killer.Race != ObjectType.Player)
            {
                for (var i = 0; i < Info.Equipment.Length; i++)
                {
                    var item = Info.Equipment[i];

                    if (item == null)
                        continue;

                    if (item.Info.Bind.HasFlag(BindMode.DontDeathdrop))
                        continue;

                    // TODO: Check this.
                    if ((item.WeddingRing != -1) && (Info.Equipment[(int)EquipmentSlot.左戒指].UniqueID == item.UniqueID))
                        continue;

                    if (item.SealedInfo != null && item.SealedInfo.ExpiryDate > Envir.Now)
                        continue;

                    if (item.Info.Bind.HasFlag(BindMode.BreakOnDeath))
                    {
                        Info.Equipment[i] = null;
                        Enqueue(new S.DeleteItem { UniqueID = item.UniqueID, Count = item.Count });
                        ReceiveChat($"物品 {item.FriendlyName} 随你死亡而消失", ChatType.System2);
                        Report.ItemChanged(item, item.Count, 1, "RedDeathDrop");
                    }

                    if (item.Count > 1)
                    {
                        var percent = Envir.RandomomRange(10, 4);
                        var count = (ushort)Math.Ceiling(item.Count / 10F * percent);

                        if (count > item.Count)
                            throw new ArgumentOutOfRangeException();

                        var temp2 = Envir.CreateFreshItem(item.Info);
                        temp2.Count = count;

                        if (!DropItem(temp2, Settings.DropRange, true))
                            continue;

                        if (count == item.Count)
                            Info.Equipment[i] = null;

                        Enqueue(new S.DeleteItem { UniqueID = item.UniqueID, Count = count });
                        item.Count -= count;

                        Report.ItemChanged(item, count, 1, "RedDeathDrop");
                    }
                    else if (Envir.Random.Next(10) == 0)
                    {
                        if (Envir.ReturnRentalItem(item, item.RentalInformation?.OwnerName, Info))
                        {
                            Info.Equipment[i] = null;
                            Enqueue(new S.DeleteItem { UniqueID = item.UniqueID, Count = item.Count });

                            ReceiveChat($"你已死亡 {item.Info.FriendlyName} 将返还给物品拥有者", ChatType.Hint);
                            Report.ItemMailed(item, 1, 1, "死亡掉落租赁物品");

                            continue;
                        }

                        if (!DropItem(item, Settings.DropRange, true))
                            continue;

                        if (item.Info.GlobalDropNotify)
                            foreach (var player in Envir.Players)
                            {
                                player.ReceiveChat($"{Name} 降级 {item.FriendlyName}", ChatType.System2);
                            }

                        Info.Equipment[i] = null;
                        Enqueue(new S.DeleteItem { UniqueID = item.UniqueID, Count = item.Count });

                        Report.ItemChanged(item, item.Count, 1, "RedDeathDrop");
                    }
                }

            }

            for (var i = 0; i < Info.Inventory.Length; i++)
            {
                var item = Info.Inventory[i];

                if (item == null)
                    continue;

                if (item.Info.Bind.HasFlag(BindMode.DontDeathdrop))
                    continue;

                if (item.WeddingRing != -1)
                    continue;

                if (item.SealedInfo != null && item.SealedInfo.ExpiryDate > Envir.Now)
                    continue;

                if (Envir.ReturnRentalItem(item, item.RentalInformation?.OwnerName, Info))
                {
                    Info.Inventory[i] = null;
                    Enqueue(new S.DeleteItem { UniqueID = item.UniqueID, Count = item.Count });

                    ReceiveChat($"你已死亡 {item.Info.FriendlyName} 将返还给物品拥有者", ChatType.Hint);
                    Report.ItemMailed(item, 1, 1, "死亡掉落租赁物品");

                    continue;
                }

                if (!DropItem(item, Settings.DropRange, true))
                    continue;

                if (item.Info.GlobalDropNotify)
                    foreach (var player in Envir.Players)
                    {
                        player.ReceiveChat($"{Name} 掉落了 {item.FriendlyName}.", ChatType.System2);
                    }

                Info.Inventory[i] = null;
                Enqueue(new S.DeleteItem { UniqueID = item.UniqueID, Count = item.Count });

                Report.ItemChanged(item, item.Count, 1, "RedDeathDrop");
            }

            RefreshStats();
        }        
        public override void WinExp(uint amount, uint targetLevel = 0)
        {
            int expPoint;
            uint originalAmount = amount;

            expPoint = ReduceExp(amount, targetLevel);
            expPoint = (int)(expPoint * Settings.ExpRate);            

            //party
            float[] partyExpRate = { 1.0F, 1.3F, 1.4F, 1.5F, 1.6F, 1.7F, 1.8F, 1.9F, 2F, 2.1F, 2.2F };

            if (GroupMembers != null)
            {
                int sumLevel = 0;
                int nearCount = 0;
                for (int i = 0; i < GroupMembers.Count; i++)
                {
                    PlayerObject player = GroupMembers[i];

                    if (Functions.InRange(player.CurrentLocation, CurrentLocation, Globals.DataRange))
                    {
                        sumLevel += player.Level;
                        nearCount++;
                    }
                }

                if (nearCount > partyExpRate.Length) nearCount = partyExpRate.Length;

                for (int i = 0; i < GroupMembers.Count; i++)
                {
                    PlayerObject player = GroupMembers[i];
                    if (player.CurrentMap == CurrentMap &&
                        Functions.InRange(player.CurrentLocation, CurrentLocation, Globals.DataRange) && !player.Dead)
                    {
                        player.GainExp((uint)((float)expPoint * partyExpRate[nearCount - 1] * (float)player.Level / (float)sumLevel));
                    }
                }
            }
            else
                GainExp((uint)expPoint);

            if (HeroSpawned && !Hero.Dead)
            {
                expPoint = Hero.ReduceExp(amount, targetLevel);
                expPoint = (int)(expPoint * Settings.ExpRate);
                Hero.GainExp((uint)expPoint);
            }
        }
        public override void GainExp(uint amount)
        {
            if (!CanGainExp) return;

            if (amount == 0) return;

            if (Info.Married != 0)
            {
                if (HasBuff(BuffType.心心相映, out Buff buff))
                {
                    CharacterInfo lover = Envir.GetCharacterInfo(Info.Married);
                    PlayerObject loverPlayer = Envir.GetPlayer(lover.Name);
                    if (loverPlayer != null && loverPlayer.CurrentMap == CurrentMap && Functions.InRange(loverPlayer.CurrentLocation, CurrentLocation, Globals.DataRange) && !loverPlayer.Dead)
                    {
                        amount += (uint)Math.Max(0, (amount * Stats[Stat.伴侣专享经验数率]) / 100);
                    }
                }
            }

            if (Info.Mentor != 0 && !Info.IsMentor)
            {
                if (HasBuff(BuffType.衣钵相传, out _))
                {
                    CharacterInfo mentor = Envir.GetCharacterInfo(Info.Mentor);
                    PlayerObject mentorPlayer = Envir.GetPlayer(mentor.Name);
                    if (mentorPlayer != null && mentorPlayer.CurrentMap == CurrentMap && Functions.InRange(mentorPlayer.CurrentLocation, CurrentLocation, Globals.DataRange) && !mentorPlayer.Dead)
                    {
                        if (GroupMembers != null && GroupMembers.Contains(mentorPlayer))
                            amount += (uint)Math.Max(0, (amount * Stats[Stat.师徒专享经验数率]) / 100);
                    }
                }
            }

            if (Stats[Stat.经验增长数率] > 0)
            {
                amount += (uint)Math.Max(0, (amount * Stats[Stat.经验增长数率]) / 100);
            }

            if (Info.Mentor != 0 && !Info.IsMentor)
            {
                MenteeEXP += (amount * Settings.MenteeExpBank) / 100;
            }    

            Experience += amount;

            Enqueue(new S.GainExperience { Amount = amount });

            for (int i = 0; i < Pets.Count; i++)
            {
                MonsterObject monster = Pets[i];
                if (monster.CurrentMap == CurrentMap && Functions.InRange(monster.CurrentLocation, CurrentLocation, Globals.DataRange) && !monster.Dead)
                    monster.PetExp(amount);
            }

            if (MyGuild != null && MyGuild.Name != Settings.NewbieGuild)
                MyGuild.GainExp(amount);

            if (Experience < MaxExperience) return;
            if (Level >= ushort.MaxValue) return;

            //Calculate increased levels
            var experience = Experience;

            while (experience >= MaxExperience)
            {
                Level++;
                experience -= MaxExperience;

                RefreshLevelStats();

                if (Level >= ushort.MaxValue) break;
            }

            Experience = experience;

            LevelUp();

            if (IsGM) return;
            if ((LastRankUpdate + 3600 * 1000) > Envir.Time)
            {
                LastRankUpdate = Envir.Time;
                Envir.CheckRankUpdate(Info);
            }
        }
        public override void LevelUp()
        {
            CallDefaultNPC(DefaultNPCType.LevelUp);

            base.LevelUp();

            Enqueue(new S.LevelChanged { Level = Level, Experience = Experience, MaxExperience = MaxExperience });

            if (Info.Mentor != 0 && !Info.IsMentor)
            {
                CharacterInfo Mentor = Envir.GetCharacterInfo(Info.Mentor);
                if ((Mentor != null) && ((Info.Level + Settings.MentorLevelGap) > Mentor.Level))
                    MentorBreak();
            }

            for (int i = CurrentMap.NPCs.Count - 1; i >= 0; i--)
            {
                if (Functions.InRange(CurrentMap.NPCs[i].CurrentLocation, CurrentLocation, Globals.DataRange))
                    CurrentMap.NPCs[i].CheckVisible(this);
            }
            Report.Levelled(Level);

            if (IsGM) return;
            Envir.CheckRankUpdate(Info);
        }        
        private void AddQuestItem(UserItem item)
        {
            if (item.Info.StackSize > 1) //Stackable
            {
                for (int i = 0; i < Info.QuestInventory.Length; i++)
                {
                    UserItem temp = Info.QuestInventory[i];
                    if (temp == null || item.Info != temp.Info || temp.Count >= temp.Info.StackSize) continue;

                    if (item.Count + temp.Count <= temp.Info.StackSize)
                    {
                        temp.Count += item.Count;
                        return;
                    }
                    item.Count -= (ushort)(temp.Info.StackSize - temp.Count);
                    temp.Count = temp.Info.StackSize;
                }
            }

            for (int i = 0; i < Info.QuestInventory.Length; i++)
            {
                if (Info.QuestInventory[i] != null) continue;
                Info.QuestInventory[i] = item;

                return;
            }
        }
        public void CheckQuestInfo(QuestInfo info)
        {
            if (Connection.SentQuestInfo.Contains(info)) return;
            Enqueue(new S.NewQuestInfo { Info = info.CreateClientQuestInfo() });
            Connection.SentQuestInfo.Add(info);
        }
        public void CheckRecipeInfo(RecipeInfo info)
        {
            if (Connection.SentRecipeInfo.Contains(info)) return;

            CheckItemInfo(info.Item.Info);

            foreach (var tool in info.Tools)
            {
                CheckItemInfo(tool.Info);
            }

            foreach (var ingredient in info.Ingredients)
            {
                CheckItemInfo(ingredient.Info);
            }

            Enqueue(new S.NewRecipeInfo { Info = info.CreateClientRecipeInfo() });
            Connection.SentRecipeInfo.Add(info);
        }
        public void CheckMapInfo(MapInfo mapInfo)
        {
            if (!Connection.WorldMapSetupSent)
            {
                Enqueue(new S.WorldMapSetupInfo { Setup = Settings.WorldMapSetup, TeleportToNPCCost = Settings.TeleportToNPCCost });
                Connection.WorldMapSetupSent = true;
            }

            if (Connection.SentMapInfo.Contains(mapInfo)) return;

            var map = Envir.GetMap(mapInfo.Index);
            if (map == null) return;

            var info = new ClientMapInfo()
            {
                Width = map.Width,
                Height = map.Height,
                BigMap = mapInfo.BigMap,
                Title = mapInfo.Title
            };

            foreach (MovementInfo mInfo in mapInfo.Movements.Where(x => x.ShowOnBigMap))
            {
                Map destMap = Envir.GetMap(mInfo.MapIndex);
                if (destMap is null)
                    continue;
                var cmInfo = new ClientMovementInfo()
                {
                    Destination = mInfo.MapIndex,
                    Location = mInfo.Source,
                    Icon = mInfo.Icon
                };
                
                cmInfo.Title = destMap.Info.Title;

                info.Movements.Add(cmInfo);
            }

            foreach (NPCObject npc in Envir.NPCs.Where(x => x.CurrentMap == map && x.Info.ShowOnBigMap).OrderBy(x => x.Info.BigMapIcon))
            {
                info.NPCs.Add(new ClientNPCInfo()
                {
                    ObjectID = npc.ObjectID,
                    Name = npc.Info.Name,
                    Location = npc.Info.Location,
                    Icon = npc.Info.BigMapIcon,
                    CanTeleportTo = npc.Info.CanTeleportTo
                });
            }

            Enqueue(new S.NewMapInfo { MapIndex = mapInfo.Index, Info = info });
            Connection.SentMapInfo.Add(mapInfo);
        }
        private void SetBind()
        {
            SafeZoneInfo szi = Envir.StartPoints[Envir.Random.Next(Envir.StartPoints.Count)];

            BindMapIndex = szi.Info.Index;
            BindLocation = szi.Location;
        }
        protected override void SetBindSafeZone(SafeZoneInfo szi)
        {
            BindLocation = szi.Location;
            BindMapIndex = CurrentMapIndex;
        }
        public void StartGame()
        {
            Map temp = Envir.GetMap(CurrentMapIndex);

            if (temp != null && temp.Info.NoReconnect)
            {
                Map temp1 = Envir.GetMapByNameAndInstance(temp.Info.NoReconnectMap);
                if (temp1 != null)
                {
                    temp = temp1;
                    CurrentLocation = GetRandomPoint(40, 0, temp);
                }
            }

            if (temp == null || !temp.ValidPoint(CurrentLocation))
            {
                temp = Envir.GetMap(BindMapIndex);

                if (temp == null || !temp.ValidPoint(BindLocation))
                {
                    SetBind();
                    temp = Envir.GetMap(BindMapIndex);

                    if (temp == null || !temp.ValidPoint(BindLocation))
                    {
                        StartGameFailed();
                        return;
                    }
                }
                CurrentMapIndex = BindMapIndex;
                CurrentLocation = BindLocation;
            }
            temp.AddObject(this);
            CurrentMap = temp;
            Envir.Players.Add(this);

            StartGameSuccess();

            //Call Login NPC
            CallDefaultNPC(DefaultNPCType.Login);

            //Call Daily NPC
            if (Info.NewDay)
            {
                CallDefaultNPC(DefaultNPCType.Daily);
            }
        }
        private void StartGameSuccess()
        {
            Connection.Stage = GameStage.Game;

            Enqueue(new S.StartGame { Result = 4, Resolution = Settings.AllowedResolution });
            ReceiveChat(string.Format(GameLanguage.Welcome, GameLanguage.GameName), ChatType.Hint);

            if (Settings.TestServer)
            {
                ReceiveChat("游戏目前处于测试模式", ChatType.Hint);
                Chat("@GAMEMASTER");
            }

            for (int i = 0; i < Info.Magics.Count; i++)
            {
                var magic = Info.Magics[i];
                magic.CastTime += Envir.Time;

                if (magic.CastTime + magic.GetDelay() < Envir.Time)
                {
                    magic.CastTime = int.MinValue;
                }
            }

            if (Info.GuildIndex != -1)
            {
                if (MyGuild == null)
                {
                    Info.GuildIndex = -1;
                    ReceiveChat("公会解散", ChatType.System);
                }
                else
                {
                    MyGuildRank = MyGuild.FindRank(Info.Name);
                    if (MyGuildRank == null)
                    {
                        MyGuild = null;
                        Info.GuildIndex = -1;
                        ReceiveChat("退出公会", ChatType.System);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(Settings.Notice.Message) && Settings.Notice.LastUpdate > Info.LastLogoutDate)
            {
                Enqueue(new S.UpdateNotice { Notice = Settings.Notice });
            }

            SetLevelEffects();

            GetItemInfo(Connection);
            GetMapInfo(Connection);
            GetUserInfo(Connection);

            Spawned();

            GetQuestInfo();
            GetRecipeInfo();

            GetCompletedQuests();

            GetMail();
            GetFriends();
            GetRelationship();

            if (Info.Mentor != 0 && Info.MentorDate.AddDays(Settings.MentorLength) < Envir.Now)
            {
                MentorBreak();
            }
            else
            {
                GetMentor();
            }

            CheckConquest();

            GetGameShop();

            for (int i = 0; i < CurrentQuests.Count; i++)
            {
                var quest = CurrentQuests[i];
                quest.Init(this);
                SendUpdateQuest(quest, QuestState.Add);
            }

            SendBaseStats();
            GetObjectsPassive();
            Enqueue(new S.TimeOfDay { Lights = Envir.Lights });
            Enqueue(new S.ChangeAMode { Mode = AMode });
            Enqueue(new S.ChangePMode { Mode = PMode });
            Enqueue(new S.SwitchGroup { AllowGroup = AllowGroup });

            Enqueue(new S.DefaultNPC { ObjectID = Envir.DefaultNPC.LoadedObjectID });

            Enqueue(new S.GuildBuffList() { GuildBuffs = Settings.Guild_BuffList });
            RequestedGuildBuffInfo = true;

            if (Info.Thrusting) Enqueue(new S.SpellToggle { ObjectID = ObjectID, Spell = Spell.Thrusting, CanUse = true });
            if (Info.HalfMoon) Enqueue(new S.SpellToggle { ObjectID = ObjectID, Spell = Spell.HalfMoon, CanUse = true });
            if (Info.CrossHalfMoon) Enqueue(new S.SpellToggle { ObjectID = ObjectID, Spell = Spell.CrossHalfMoon, CanUse = true });
            if (Info.DoubleSlash) Enqueue(new S.SpellToggle { ObjectID = ObjectID, Spell = Spell.DoubleSlash, CanUse = true });

            for (int i = 0; i < Info.Pets.Count; i++)
            {
                MonsterObject monster;

                PetInfo info = Info.Pets[i];

                var monsterInfo = Envir.GetMonsterInfo(info.MonsterIndex);
                if (monsterInfo == null) continue;

                monster = MonsterObject.GetMonster(monsterInfo);
                if (monster == null) continue;

                monster.PetLevel = info.Level;
                monster.MaxPetLevel = info.MaxPetLevel;
                monster.PetExperience = info.Experience;
                monster.Master = this;

                switch (Settings.PetSave)
                {
                    case true when Settings.PetSave is true:

                        if (monster.Info.Name == Settings.CloneName)
                        {
                            monster.ActionTime = Envir.Time + 1000;
                            monster.RefreshNameColour(false);
                        }

                        break;
                    case false when Settings.PetSave is false:

                        switch (Class)
                        {
                            case (MirClass.法师):

                                if (monster.Info.Name == Settings.CloneName)
                                {
                                    monster.ActionTime = Envir.Time + 1000;
                                    monster.RefreshNameColour(false);
                                }
                                else
                                {
                                    monster.TameTime = Envir.Time + info.TameTime;
                                }

                                break;
                        }

                        break;
                }

                // [grimchamp] leave refresh here incase future code sets levels or stats in above switch
                monster.RefreshAll(); 

                Pets.Add(monster);

                if (!monster.Spawn(CurrentMap, Back))
                {
                    monster.Spawn(CurrentMap, CurrentLocation);
                }

                monster.SetHP(info.HP);
            }

            Info.Pets.Clear();

            for (int i = 0; i < Buffs.Count; i++)
            {
                var buff = Buffs[i];
                buff.LastTime = Envir.Time;
                buff.ObjectID = ObjectID;

                AddBuff(buff.Type, null, (int)buff.ExpireTime, buff.Stats, true, true, buff.Values);   
            }

            for (int i = 0; i < PoisonList.Count; i++)
            {
                var poison = PoisonList[i];
                poison.TickTime = Envir.Time;
            }

            if (MyGuild != null)
            {
                MyGuild.PlayerLogged(this, true);
                if (MyGuild.BuffList.Count > 0)
                {
                    Enqueue(new S.GuildBuffList() { ActiveBuffs = MyGuild.BuffList });
                }
            }

            if (!HeroSpawned)
            {
                RemoveBuff(BuffType.英雄灵气);
            }

            if (InSafeZone && Info.LastLogoutDate > DateTime.MinValue)
            {
                double totalMinutes = (Envir.Now - Info.LastLogoutDate).TotalMinutes;

                _restedCounter = (int)(totalMinutes * 60);
            }

            if (Info.Mail.Count > Settings.MailCapacity)
            {
                ReceiveChat("邮箱已满", ChatType.System);
            }

            Report.Connected(Connection.IPAddress);

            MessageQueue.Enqueue(string.Format("{0} 已连接", Info.Name));

            if (IsGM)
            {
                UpdateGMBuff();
            }
            else
            {
                LastRankUpdate = Envir.Time;
                Envir.CheckRankUpdate(Info);
                Envir.OnlineRankingCount[0]++;
                Envir.OnlineRankingCount[(int)Class + 1]++;
            }
        }
        private void StartGameFailed()
        {
            Enqueue(new S.StartGame { Result = 3 });
            CleanUp();
        }        
        public void GiveRestedBonus(int count)
        {
            if (count > 0)
            {
                long existingTime = 0;

                if (HasBuff(BuffType.精力充沛, out Buff rested))
                {
                    existingTime = rested.ExpireTime;
                }

                int duration = (int)Math.Min(int.MaxValue, ((Settings.RestedBuffLength * Settings.Minute) * count) + existingTime);
                int maxDuration = (Settings.RestedBuffLength * Settings.Minute) * Settings.RestedMaxBonus;

                if (duration > maxDuration) duration = maxDuration;

                AddBuff(BuffType.精力充沛, this, duration, new Stats { [Stat.经验增长数率] = Settings.RestedExpBonus });

                _restedCounter = 0;
            }
        }
        public override void Revive(int hp, bool effect)
        {
            if (!Dead) return;

            base.Revive(hp, effect);

            GetObjects();

            Fishing = false;
            Enqueue(GetFishInfo());
            GroupMemberMapNameChanged();
            GetPlayerLocation();
        }
        public void TownRevive()
        {
            if (!Dead) return;

            Map temp = Envir.GetMap(BindMapIndex);
            Point bindLocation = BindLocation;

            if (Info.PKPoints >= 200)
            {
                temp = Envir.GetMapByNameAndInstance(Settings.PKTownMapName, 1);
                bindLocation = new Point(Settings.PKTownPositionX, Settings.PKTownPositionY);

                if (temp == null)
                {
                    temp = Envir.GetMap(BindMapIndex);
                    bindLocation = BindLocation;
                }
            }

            if (temp == null || !temp.ValidPoint(bindLocation)) return;

            Dead = false;
            SetHP(Stats[Stat.HP]);
            SetMP(Stats[Stat.MP]);
            RefreshStats();

            CurrentMap.RemoveObject(this);
            Broadcast(new S.ObjectRemove { ObjectID = ObjectID });

            CurrentMap = temp;
            CurrentLocation = bindLocation;

            CurrentMap.AddObject(this);

            Enqueue(new S.MapChanged
            {
                MapIndex = CurrentMap.Info.Index,
                FileName = CurrentMap.Info.FileName,
                Title = CurrentMap.Info.Title,
                Weather = CurrentMap.Info.WeatherParticles,
                MiniMap = CurrentMap.Info.MiniMap,
                BigMap = CurrentMap.Info.BigMap,
                Lights = CurrentMap.Info.Light,
                Location = CurrentLocation,
                Direction = Direction,
                MapDarkLight = CurrentMap.Info.MapDarkLight,
                Music = CurrentMap.Info.Music
            });

            GetObjects();
            Enqueue(new S.Revived());
            Broadcast(new S.ObjectRevived { ObjectID = ObjectID, Effect = true });

            InSafeZone = true;
            Fishing = false;
            Enqueue(GetFishInfo());
            GroupMemberMapNameChanged();
            GetPlayerLocation();
        }
        public override bool Teleport(Map temp, Point location, bool effects = true, byte effectnumber = 0)
        {
            Map oldMap = CurrentMap;
            Point oldLocation = CurrentLocation;
            bool mapChanged = temp != oldMap;

            if (!base.Teleport(temp, location, effects)) return false;            

            //Cancel actions
            if (TradePartner != null)
                TradeCancel();

            if (ItemRentalPartner != null)
                CancelItemRental();

            GetObjectsPassive();

            CheckConquest();

            Fishing = false;
            Enqueue(GetFishInfo());

            if (mapChanged)
            {
                CallDefaultNPC(DefaultNPCType.MapEnter, CurrentMap.Info.FileName);

                if (Info.Married != 0)
                {
                    CharacterInfo Lover = Envir.GetCharacterInfo(Info.Married);
                    PlayerObject player = Envir.GetPlayer(Lover.Name);

                    if (player != null) player.GetRelationship(false);
                }
                GroupMemberMapNameChanged();
            }
            GetPlayerLocation();

            Report?.MapChange(oldMap.Info, CurrentMap.Info);

            return true;
        }

        static readonly ServerPacketIds[] BroadcastObservePackets = new ServerPacketIds[]
        {
            ServerPacketIds.ObjectTurn,
            ServerPacketIds.ObjectWalk,
            ServerPacketIds.ObjectRun,
            ServerPacketIds.ObjectAttack,
            ServerPacketIds.ObjectRangeAttack,
            ServerPacketIds.ObjectMagic,
            ServerPacketIds.ObjectHarvest
        };

        public override void Broadcast(Packet p)
        {
            if (p == null || CurrentMap == null) return;

            base.Broadcast(p);

            if (Array.Exists(BroadcastObservePackets, x => x == (ServerPacketIds)p.Index))
            {
                foreach (MirConnection c in Connection.Observers)
                    c.Enqueue(p);
            }
        }

        public void AddObserver(MirConnection observer)
        {
            if (observer == Connection) return;

            Connection.AddObserver(observer);
            GetItemInfo(observer);
            GetMapInfo(observer);
            GetUserInfo(observer);
            GetObjectsPassive(observer);
            if (observer.Player != null)
                observer.Player.StopGame(24);            
        }
        protected virtual void GetItemInfo(MirConnection c)
        {
            UserItem item;
            for (int i = 0; i < Info.Inventory.Length; i++)
            {
                item = Info.Inventory[i];
                if (item == null) continue;

                c.CheckItem(item);
            }

            for (int i = 0; i < Info.Equipment.Length; i++)
            {
                item = Info.Equipment[i];

                if (item == null) continue;

                c.CheckItem(item);
            }

            for (int i = 0; i < Info.QuestInventory.Length; i++)
            {
                item = Info.QuestInventory[i];

                if (item == null) continue;
                c.CheckItem(item);
            }
        }
        private void GetUserInfo(MirConnection c)
        {
            string guildname = MyGuild != null ? MyGuild.Name : "";
            string guildrank = MyGuild != null ? MyGuildRank.Name : "";
            S.UserInformation packet = new S.UserInformation
            {
                ObjectID = ObjectID,
                RealId = (uint)Info.Index,
                Name = Name,
                GuildName = guildname,
                GuildRank = guildrank,
                NameColour = GetNameColour(this),
                Class = Class,
                Gender = Gender,
                Level = Level,
                Location = CurrentLocation,
                Direction = Direction,
                Hair = Hair,
                HP = HP,
                MP = MP,

                Experience = Experience,
                MaxExperience = MaxExperience,

                LevelEffects = LevelEffects,

                HasHero = HasHero,
                HeroBehaviour = Info.HeroBehaviour,

                Inventory = new UserItem[Info.Inventory.Length],
                Equipment = new UserItem[Info.Equipment.Length],
                QuestInventory = new UserItem[Info.QuestInventory.Length],
                Gold = Account.Gold,
                Credit = Account.Credit,
                HasExpandedStorage = Account.ExpandedStorageExpiryDate > Envir.Now ? true : false,
                ExpandedStorageExpiryTime = Account.ExpandedStorageExpiryDate,
                AllowObserve = AllowObserve,
                Observer = c != Connection
            };

            //Copy this method to prevent modification before sending packet information.
            for (int i = 0; i < Info.Magics.Count; i++)
                packet.Magics.Add(Info.Magics[i].CreateClientMagic());

            Info.Inventory.CopyTo(packet.Inventory, 0);
            Info.Equipment.CopyTo(packet.Equipment, 0);
            Info.QuestInventory.CopyTo(packet.QuestInventory, 0);

            for (int i = 0; i < Info.IntelligentCreatures.Count; i++)
            {
                packet.IntelligentCreatures.Add(Info.IntelligentCreatures[i].CreateClientIntelligentCreature());
            }

            packet.SummonedCreatureType = SummonedCreatureType;
            packet.CreatureSummoned = CreatureSummoned;

            Enqueue(packet, c);
        }        
        private void GetMapInfo(MirConnection c)
        {
            Enqueue(new S.MapInformation
            {
                MapIndex = CurrentMap.Info.Index,
                FileName = CurrentMap.Info.FileName,
                Title = CurrentMap.Info.Title,
                MiniMap = CurrentMap.Info.MiniMap,
                Lights = CurrentMap.Info.Light,
                BigMap = CurrentMap.Info.BigMap,
                WeatherParticles = CurrentMap.Info.WeatherParticles,
                Lightning = CurrentMap.Info.Lightning,
                Fire = CurrentMap.Info.Fire,
                MapDarkLight = CurrentMap.Info.MapDarkLight,
                Music = CurrentMap.Info.Music,
            }, c);
        }
        private void GetQuestInfo()
        {
            for (int i = 0; i < Envir.QuestInfoList.Count; i++)
            {
                CheckQuestInfo(Envir.QuestInfoList[i]);
            }
        }
        private void GetRecipeInfo()
        {
            for (int i = 0; i < Envir.RecipeInfoList.Count; i++)
            {
                CheckRecipeInfo(Envir.RecipeInfoList[i]);
            }
        }
        private void GetObjects()
        {
            for (int y = CurrentLocation.Y - Globals.DataRange; y <= CurrentLocation.Y + Globals.DataRange; y++)
            {
                if (y < 0) continue;
                if (y >= CurrentMap.Height) break;

                for (int x = CurrentLocation.X - Globals.DataRange; x <= CurrentLocation.X + Globals.DataRange; x++)
                {
                    if (x < 0) continue;
                    if (x >= CurrentMap.Width) break;
                    if (x < 0 || x >= CurrentMap.Width) continue;

                    Cell cell = CurrentMap.GetCell(x, y);

                    if (!cell.Valid || cell.Objects == null) continue;

                    for (int i = 0; i < cell.Objects.Count; i++)
                    {
                        MapObject ob = cell.Objects[i];

                        //if (ob.Race == ObjectType.Player && ob.Observer) continue;

                        ob.Add(this);
                    }
                }
            }
        }
        private void GetObjectsPassive(MirConnection c = null)
        {
            for (int y = CurrentLocation.Y - Globals.DataRange; y <= CurrentLocation.Y + Globals.DataRange; y++)
            {
                if (y < 0) continue;
                if (y >= CurrentMap.Height) break;

                for (int x = CurrentLocation.X - Globals.DataRange; x <= CurrentLocation.X + Globals.DataRange; x++)
                {
                    if (x < 0) continue;
                    if (x >= CurrentMap.Width) break;
                    if (x < 0 || x >= CurrentMap.Width) continue;

                    Cell cell = CurrentMap.GetCell(x, y);

                    if (!cell.Valid || cell.Objects == null) continue;

                    for (int i = 0; i < cell.Objects.Count; i++)
                    {
                        MapObject ob = cell.Objects[i];
                        if (ob == this) continue;

                        if (ob.Race == ObjectType.Player)
                        {
                            PlayerObject Player = (PlayerObject)ob;
                            Enqueue(Player.GetInfoEx(this), c);
                        }
                        else if (ob.Race == ObjectType.Spell)
                        {
                            SpellObject obSpell = (SpellObject)ob;

                            if ((obSpell.Spell != Spell.ExplosiveTrap) || (obSpell.Caster != null && IsFriendlyTarget(obSpell.Caster)))
                                Enqueue(ob.GetInfo(), c);
                        }
                        else if (ob.Race == ObjectType.Merchant)
                        {
                            NPCObject NPC = (NPCObject)ob;

                            NPC.CheckVisible(this);

                            if (NPC.VisibleLog[Info.Index] && NPC.Visible) Enqueue(ob.GetInfo(), c);
                        }
                        else
                        {
                            Enqueue(ob.GetInfo(), c);
                        }

                        if (ob.Race == ObjectType.Player || ob.Race == ObjectType.Monster || ob.Race == ObjectType.Hero)
                        {
                            ob.SendHealth(this);
                        }
                    }
                }
            }
        }
        public override void RefreshGuildBuffs()
        {
            if (MyGuild == null) return;
            if (MyGuild.BuffList.Count == 0) return;

            for (int i = 0; i < MyGuild.BuffList.Count; i++)
            {
                GuildBuff buff = MyGuild.BuffList[i];
                if ((buff.Info == null) || (!buff.Active)) continue;

                Stats.Add(buff.Info.Stats);
            }
        }

        public override void RefreshNameColour()
        {
            var prevColor = NameColour;
            NameColour = GetNameColour(this);
            
            if (prevColor == NameColour) return;
            
            Enqueue(new S.ColourChanged { NameColour = NameColour });
            BroadcastColourChange();
        }

        public override Color GetNameColour(HumanObject human)
        {
            if (human == null) return NameColour;

            if (human is PlayerObject player)
            {
                if (player.PKPoints >= 200)
                    return Color.Red;

                if (Envir.Time < player.BrownTime)
                    return Color.SaddleBrown;

                if (player.WarZone)
                {
                    if (player.MyGuild == null)
                        return Color.Green;

                    if (player.MyGuild == MyGuild)
                        return Color.Green;
                    else
                        return Color.Orange;
                }

                if (MyGuild != null)
                {
                    if (MyGuild.IsAtWar())
                    {
                        if (player.MyGuild != null)
                        {
                            if (player.MyGuild == MyGuild)
                                return Color.Blue;
                            if (MyGuild.IsEnemy(player.MyGuild))
                                return Color.Orange;
                        }
                    }
                }

                if (player.PKPoints >= 100)
                    return Color.Yellow;
            }

            return Color.White;
        }
        public void Chat(string message, List<ChatItem> linkedItems = null)
        {
            if (string.IsNullOrEmpty(message)) return;

            MessageQueue.EnqueueChat(string.Format("{0}: {1}", Name, message));

            if (GMLogin)
            {
                if (message == GMPassword)
                {
                    IsGM = true;
                    UpdateGMBuff();
                    MessageQueue.Enqueue(string.Format("{0} 现在是游戏管理员身份", Name));
                    ReceiveChat("升级为游戏管理员", ChatType.System);
                    Envir.RemoveRank(Info);//remove gm chars from ranking to avoid causing bugs in rank list
                }
                else
                {
                    MessageQueue.Enqueue(string.Format("{0} 试图以游戏管理员身份登录", Name));
                    ReceiveChat("登录密码不正确", ChatType.System);
                }
                GMLogin = false;
                return;
            }

            if (Info.ChatBanned)
            {
                if (Info.ChatBanExpiryDate > Envir.Now)
                {
                    ReceiveChat("被禁止聊天", ChatType.System);
                    return;
                }

                Info.ChatBanned = false;
            }
            else
            {
                if (ChatTime > Envir.Time)
                {
                    if (ChatTick >= 5 & !IsGM)
                    {
                        Info.ChatBanned = true;
                        Info.ChatBanExpiryDate = Envir.Now.AddMinutes(5);
                        ReceiveChat("被禁止聊天5分钟", ChatType.System);
                        return;
                    }

                    ChatTick++;
                }
                else
                    ChatTick = 0;

                ChatTime = Envir.Time + 2000;
            }

            string[] parts;

            message = message.Replace("$pos", Functions.PointToString(CurrentLocation));


            Packet p;
            if (message.StartsWith("/"))
            {
                //Private Message
                message = message.Remove(0, 1);
                parts = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length == 0) return;

                PlayerObject player = Envir.GetPlayer(parts[0]);

                if (player == null)
                {
                    IntelligentCreatureObject creature = GetCreatureByName(parts[0]);
                    if (creature != null)
                    {
                        creature.ReceiveChat(message.Remove(0, parts[0].Length), ChatType.WhisperIn);
                        return;
                    }
                    ReceiveChat(string.Format("未找到 {0}.", parts[0]), ChatType.System);
                    return;
                }

                if (player.Info.Friends.Any(e => e.Info == Info && e.Blocked))
                {
                    ReceiveChat("玩家不接受你的消息", ChatType.System);
                    return;
                }

                if (Info.Friends.Any(e => e.Info == player.Info && e.Blocked))
                {
                    ReceiveChat("当玩家在你的黑名单上时，无法向其发送消息", ChatType.System);
                    return;
                }

                message = ProcessChatItems(message, new List<PlayerObject> { player }, linkedItems);

                ReceiveChat(string.Format("/{0}", message), ChatType.WhisperOut);
                player.ReceiveChat(string.Format("{0}=>{1}", Name, message.Remove(0, parts[0].Length)), ChatType.WhisperIn);
            }
            else if (message.StartsWith("!!"))
            {
                if (GroupMembers == null) return;
                //Group
                message = String.Format("{0}:{1}", Name, message.Remove(0, 2));

                message = ProcessChatItems(message, GroupMembers, linkedItems);

                p = new S.ObjectChat { ObjectID = ObjectID, Text = message, Type = ChatType.Group };

                for (int i = 0; i < GroupMembers.Count; i++)
                    GroupMembers[i].Enqueue(p);
            }
            else if (message.StartsWith("!~"))
            {
                if (MyGuild == null) return;

                //Guild
                message = message.Remove(0, 2);

                message = ProcessChatItems(message, MyGuild.GetOnlinePlayers(), linkedItems);

                MyGuild.SendMessage(String.Format("{0}: {1}", Name, message));

            }
            else if (message.StartsWith("!#"))
            {
                //Mentor Message
                message = message.Remove(0, 2);
                parts = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length == 0) return;

                if (Info.Mentor == 0) return;

                CharacterInfo Mentor = Envir.GetCharacterInfo(Info.Mentor);
                PlayerObject player = Envir.GetPlayer(Mentor.Name);

                if (player == null)
                {
                    ReceiveChat(string.Format("{0} 不在线", Mentor.Name), ChatType.System);
                    return;
                }

                message = ProcessChatItems(message, new List<PlayerObject> { player }, linkedItems);

                ReceiveChat(string.Format("{0}: {1}", Name, message), ChatType.Mentor);
                player.ReceiveChat(string.Format("{0}: {1}", Name, message), ChatType.Mentor);
            }
            else if (message.StartsWith("!"))
            {
                //Shout
                if (Envir.Time < ShoutTime)
                {
                    ReceiveChat(string.Format(" {0} 秒后再次喊话", Math.Ceiling((ShoutTime - Envir.Time) / 1000D)), ChatType.System);
                    return;
                }
                if (Level < 8 && (!HasMapShout && !HasServerShout))
                {
                    ReceiveChat("需要达到8级才能喊话", ChatType.System);
                    return;
                }

                ShoutTime = Envir.Time + 10000;
                message = String.Format("(!){0}:{1}", Name, message.Remove(0, 1));

                if (HasMapShout)
                {
                    message = ProcessChatItems(message, CurrentMap.Players, linkedItems);

                    p = new S.Chat { Message = message, Type = ChatType.Shout2 };
                    HasMapShout = false;

                    for (int i = 0; i < CurrentMap.Players.Count; i++)
                    {
                        CurrentMap.Players[i].Enqueue(p);
                    }
                    return;
                }
                else if (HasServerShout)
                {
                    message = ProcessChatItems(message, Envir.Players, linkedItems);

                    p = new S.Chat { Message = message, Type = ChatType.Shout3 };
                    HasServerShout = false;

                    for (int i = 0; i < Envir.Players.Count; i++)
                    {
                        Envir.Players[i].Enqueue(p);
                    }
                    return;
                }
                else
                {
                    List<PlayerObject> playersInRange = new List<PlayerObject>();

                    for (int i = 0; i < CurrentMap.Players.Count; i++)
                    {
                        if (!Functions.InRange(CurrentLocation, CurrentMap.Players[i].CurrentLocation, Globals.DataRange * 2)) continue;

                        playersInRange.Add(CurrentMap.Players[i]);
                    }

                    message = ProcessChatItems(message, playersInRange, linkedItems);

                    p = new S.Chat { Message = message, Type = ChatType.Shout };

                    for (int i = 0; i < playersInRange.Count; i++)
                    {
                        playersInRange[i].Enqueue(p);
                    }

                }

            }
            else if (message.StartsWith(":)"))
            {
                //Relationship Message
                message = message.Remove(0, 2);
                parts = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length == 0) return;

                if (Info.Married == 0) return;

                CharacterInfo Lover = Envir.GetCharacterInfo(Info.Married);
                PlayerObject player = Envir.GetPlayer(Lover.Name);
            
                if (player == null)
                {
                    ReceiveChat(string.Format("{0} 不在线", Lover.Name), ChatType.System);
                    return;
                }

                message = ProcessChatItems(message, new List<PlayerObject> { player }, linkedItems);

                ReceiveChat(string.Format("{0}: {1}", Name, message), ChatType.Relationship);
                player.ReceiveChat(string.Format("{0}: {1}", Name, message), ChatType.Relationship);
            }
            else if (message.StartsWith("@!"))
            {
                if (!IsGM) return;

                message = String.Format("(*){0}:{1}", Name, message.Remove(0, 2));

                message = ProcessChatItems(message, Envir.Players, linkedItems);

                p = new S.Chat { Message = message, Type = ChatType.Announcement };

                Envir.Broadcast(p);
            }
            else if (message.StartsWith("@"))
            {
                
                //Command
                message = message.Remove(0, 1);
                parts = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length == 0) return;

                PlayerObject player;
                CharacterInfo data;
                String hintstring;
                UserItem item;

                List<int> conquestAIs = new()
                {
                    //243,//72, // siege gate
                    //244,//73, // gate west
                    960,//80, // archer
                    950,//81, // gate 
                    951,//82  // wall
                };

                switch (parts[0].ToUpper())
                {
                    case "LOGIN":
                        GMLogin = true;
                        ReceiveChat("请输入管理员密码！", ChatType.Hint);
                        return;

                    case "KILL":
                        if (!IsGM) return;

                        if (parts.Length >= 2)
                        {
                            player = Envir.GetPlayer(parts[1]);

                            if (player == null)
                            {
                                ReceiveChat(string.Format("找不到 {0}", parts[0]), ChatType.System);
                                return;
                            }

                            if (!player.GMNeverDie)
                            {
                                player.Die();

                                Helpers.ChatSystem.SystemMessage(chatMessage: $" 管理员: {Name} 杀死了 {player}");
                            }
                        }
                        else
                        {
                            if (!CurrentMap.ValidPoint(Front)) return;

                            Cell cell = CurrentMap.GetCell(Front);

                            if (cell == null || cell.Objects == null) return;

                            for (int i = 0; i < cell.Objects.Count; i++)
                            {
                                MapObject ob = cell.Objects[i];

                                switch (ob.Race)
                                {
                                    case ObjectType.Player:
                                    case ObjectType.Monster:
                                        if (ob.Dead) continue;
                                        ob.EXPOwner = this;
                                        ob.ExpireTime = Envir.Time + MonsterObject.EXPOwnerDelay;
                                        ob.Die();
                                        break;
                                    default:
                                        continue;
                                }
                            }
                        }
                        return;

                    case "CHANGEGENDER":
                        if (!IsGM && !Settings.TestServer) return;

                        data = parts.Length < 2 ? Info : Envir.GetCharacterInfo(parts[1]);

                        if (data == null) return;

                        switch (data.Gender)
                        {
                            case MirGender.男性:
                                data.Gender = MirGender.女性;
                                break;
                            case MirGender.女性:
                                data.Gender = MirGender.男性;
                                break;
                        }

                        ReceiveChat(string.Format("玩家 {0} 已改为 {1}", data.Name, data.Gender), ChatType.System);
                        MessageQueue.Enqueue(string.Format("玩家使用 {2} 将 {0} 已改为 {1}", data.Name, data.Gender, Name));

                        Helpers.ChatSystem.SystemMessage(chatMessage: $"{data.Player.Name} 将性别更改为 {data.Gender.ToString()} by GM: {Name}");

                        if (data.Player != null)
                            data.Player.Connection.LogOut();

                        break;

                    case "LEVEL":
                        if ((!IsGM && !Settings.TestServer) || parts.Length < 2) return;

                        ushort level;
                        ushort old;
                        if (parts.Length >= 3)
                        {
                            if (!IsGM) return;

                            if (ushort.TryParse(parts[2], out level))
                            {
                                if (level == 0) return;
                                player = Envir.GetPlayer(parts[1]);
                                if (player == null) return;
                                old = player.Level;
                                player.Level = level;
                                player.LevelUp();

                                ReceiveChat(string.Format("角色 {0} 等级 {1} -> {2}", player.Name, old, player.Level), ChatType.System);
                                MessageQueue.Enqueue(string.Format("游戏管理员:{3} 将角色:{0} 从{1}->{2}级", player.Name, old, player.Level, Name));
                                Helpers.ChatSystem.SystemMessage(chatMessage: $"Player {player.Name} has been Levelled: {old} -> {player.Level} by GM: {Name}");

                                return;
                            }
                        }
                        else
                        {
                            if (parts[1] == "-1")
                            {
                                parts[1] = ushort.MaxValue.ToString();
                            }

                            if (ushort.TryParse(parts[1], out level))
                            {
                                if (level == 0) return;
                                old = Level;
                                Level = level;
                                LevelUp();

                                ReceiveChat(string.Format("{0} {1} -> {2}", GameLanguage.LevelUp, old, Level), ChatType.System);
                                MessageQueue.Enqueue(string.Format("游戏管理员:{3} 将角色:{0} 从{1}调整到{2}级", Name, old, Level, Name));
                                return;
                            }
                        }

                        ReceiveChat("无法调整玩家等级", ChatType.System);
                        break;

                    case "LEVELHERO":
                        if ((!IsGM && !Settings.TestServer) || parts.Length < 2) return;

                        if (parts.Length >= 3)
                        {
                            if (!IsGM) return;

                            if (ushort.TryParse(parts[2], out level))
                            {
                                if (level == 0) return;
                                player = Envir.GetPlayer(parts[1]);
                                if (player == null) return;
                                HeroObject hero = player.GetHero();
                                if (hero == null) return;
                                old = hero.Level;
                                hero.Level = level;
                                hero.LevelUp();

                                ReceiveChat(string.Format("{0}的英雄等级 {1} -> {2}", player.Name, old, hero.Level), ChatType.System);
                                MessageQueue.Enqueue(string.Format("游戏管理员:{3} 将玩家{0}的英雄等级由{1}调整到{2}", player.Name, old, hero.Level, Name));
                                Helpers.ChatSystem.SystemMessage(chatMessage: $"Player {player.Name}'s hero has been Levelled: {old} -> {hero.Level} by GM: {Name}");
                                return;
                            }
                        }
                        else
                        {
                            HeroObject hero = GetHero();
                            if (hero == null) return;

                            if (parts[1] == "-1")
                            {
                                parts[1] = ushort.MaxValue.ToString();
                            }

                            if (ushort.TryParse(parts[1], out level))
                            {
                                if (level == 0) return;
                                old = hero.Level;
                                hero.Level = level;
                                hero.LevelUp();

                                ReceiveChat(string.Format("{0} {1} -> {2}", GameLanguage.LevelUp, old, hero.Level), ChatType.System);
                                MessageQueue.Enqueue(string.Format("游戏管理员:{3} 将玩家{0}的英雄等级由{1}调整到{2}", Name, old, hero.Level, Name));
                                return;
                            }
                        }

                        ReceiveChat("等级调整失败", ChatType.System);
                        break;

                    case "MAKE":
                        {
                            if ((!IsGM && !Settings.TestServer) || parts.Length < 2) return;

                            ItemInfo iInfo;
                            int itemIndex = 0;

                            if (Int32.TryParse(parts[1], out itemIndex))
                            {
                                iInfo = Envir.GetItemInfo(itemIndex);
                            }
                            else
                            {
                                iInfo = Envir.GetItemInfo(parts[1]);
                            }

                            if (iInfo == null) return;

                            ushort itemCount = 1;
                            if (parts.Length >= 3 && !ushort.TryParse(parts[2], out itemCount))
                                itemCount = 1;

                            var tempCount = itemCount;

                            while (itemCount > 0)
                            {
                                if (iInfo.StackSize >= itemCount)
                                {
                                    item = Envir.CreateDropItem(iInfo);
                                    item.GMMade = true;
                                    item.Count = itemCount;
                                    
                                    if (CanGainItem(item)) GainItem(item);

                                    return;
                                }
                                item = Envir.CreateDropItem(iInfo);
                                item.GMMade = true;
                                item.Count = iInfo.StackSize;                               
                                itemCount -= iInfo.StackSize;

                                if (!CanGainItem(item)) return;
                                GainItem(item);
                            }

                            ReceiveChat(string.Format("{0} x{1} 已被制造", iInfo.FriendlyName, tempCount), ChatType.System);
                            MessageQueue.Enqueue(string.Format("玩家 {0} 试图制造 {1} x{2}", Name, iInfo.Name, tempCount));
                        }
                        break;
                    case "CLEARBUFFS":
                        foreach (var buff in Buffs)
                        {
                            buff.FlagForRemoval = true;
                            buff.ExpireTime = 0;
                        }
                        break;

                    case "CLEARBAG":
                        if (!IsGM && !Settings.TestServer) return;
                        player = this;

                        if (parts.Length >= 2)
                            player = Envir.GetPlayer(parts[1]);

                        if (player == null) return;
                        for (int i = 0; i < player.Info.Inventory.Length; i++)
                        {
                            item = player.Info.Inventory[i];
                            if (item == null) continue;

                            player.Enqueue(new S.DeleteItem { UniqueID = item.UniqueID, Count = item.Count });
                            player.Info.Inventory[i] = null;
                        }
                        player.RefreshStats();
                        break;

                    case "SUPERMAN":
                        if (!IsGM && !Settings.TestServer) return;

                        GMNeverDie = !GMNeverDie;

                        hintstring = GMNeverDie ? "无敌模式" : "正常模式";
                        ReceiveChat(hintstring, ChatType.Hint);
                        UpdateGMBuff();
                        break;

                    case "GAMEMASTER":
                        if (!IsGM && !Settings.TestServer) return;

                        GMGameMaster = !GMGameMaster;

                        hintstring = GMGameMaster ? "管理员模式" : "正常模式";
                        ReceiveChat(hintstring, ChatType.Hint);
                        UpdateGMBuff();
                        break;

                    case "OBSERVER":
                        if (!IsGM) return;
                        Observer = !Observer;

                        hintstring = Observer ? "观察模式" : "正常模式";
                        ReceiveChat(hintstring, ChatType.Hint);
                        UpdateGMBuff();
                        break;
                    case "加入行会":
                        EnableGuildInvite = !EnableGuildInvite;
                        hintstring = EnableGuildInvite ? "开启行会邀请" : "关闭行会邀请";
                        ReceiveChat(hintstring, ChatType.Hint);
                        break;
                    case "RECALL":
                        if (!IsGM) return;

                        if (parts.Length < 2) return;
                        player = Envir.GetPlayer(parts[1]);

                        if (player == null) return;

                        player.Teleport(CurrentMap, Front);
                        break;
                    case "OBSERVE":
                        if (parts.Length < 2) return;
                        player = Envir.GetPlayer(parts[1]);

                        if (player == null) return;
                        if ((!player.AllowObserve || !Settings.AllowObserve) && !IsGM) return;
                        
                        player.AddObserver(Connection);
                        break;
                    case "经天纬地":
                        EnableGroupRecall = !EnableGroupRecall;
                        hintstring = EnableGroupRecall ? "允许记忆传送" : "拒绝记忆传送";
                        ReceiveChat(hintstring, ChatType.Hint);
                        break;

                    case "天人合一":
                        if (GroupMembers == null || GroupMembers[0] != this || Dead)
                            return;

                        if (CurrentMap.Info.NoRecall)
                        {
                            ReceiveChat("地图禁止记忆传送", ChatType.System);
                            return;
                        }

                        if (Envir.Time < LastRecallTime)
                        {
                            ReceiveChat(string.Format("记忆套装传送再次使用 {0} 秒", (LastRecallTime - Envir.Time) / 1000), ChatType.System);
                            return;
                        }

                        if (ItemSets.Any(set => set.Set == ItemSet.记忆套装 && set.SetComplete))
                        {
                            LastRecallTime = Envir.Time + 180000;
                            for (var i = 1; i < GroupMembers.Count(); i++)
                            {
                                if (GroupMembers[i].EnableGroupRecall)
                                    GroupMembers[i].Teleport(CurrentMap, CurrentLocation);
                                else
                                    GroupMembers[i].ReceiveChat("组长使用记忆套装发起传送召唤...开启命令<@经天纬地>",
                                        ChatType.System);
                            }
                        }
                        break;
                    //case "RECALLMEMBER":
                    //    if (GroupMembers == null || GroupMembers[0] != this)
                    //    {
                    //        ReceiveChat("你不是组长", ChatType.System);
                    //        return;
                    //    }

                    //    if (Dead)
                    //    {
                    //        ReceiveChat("死后无法使用记忆传送", ChatType.System);
                    //        return;
                    //    }

                    //    if (CurrentMap.Info.NoRecall)
                    //    {
                    //        ReceiveChat("地图禁用记忆传送", ChatType.System);
                    //        return;
                    //    }

                    //    if (Envir.Time < LastRecallTime)
                    //    {
                    //        ReceiveChat(string.Format("记忆传送再次使用 {0} 秒", (LastRecallTime - Envir.Time) / 1000), ChatType.System);
                    //        return;
                    //    }
                    //    if (ItemSets.Any(set => set.Set == ItemSet.记忆套装 && set.SetComplete))
                    //    {
                    //        if (parts.Length < 2) return;
                    //        player = Envir.GetPlayer(parts[1]);

                    //        if (player == null || !IsMember(player) || this == player)
                    //        {
                    //            ReceiveChat((string.Format(" {0} 非组内成员", parts[1])), ChatType.System);
                    //            return;
                    //        }
                    //        if (!player.EnableGroupRecall)
                    //        {
                    //            player.ReceiveChat("组长发起记忆传送邀请...开启<@enablegrouprecall>",
                    //                    ChatType.System);
                    //            ReceiveChat((string.Format("{0} 未开启记忆传送", player.Name)), ChatType.System);
                    //            return;
                    //        }
                    //        LastRecallTime = Envir.Time + 60000;

                    //        if (!player.Teleport(CurrentMap, Front))
                    //            player.Teleport(CurrentMap, CurrentLocation);
                    //    }
                    //    else
                    //    {
                    //        ReceiveChat("无法使用记忆传送", ChatType.System);
                    //        return;
                    //    }
                    //    break;

                    case "心心相映":
                        if (Info.Married == 0)
                        {
                            ReceiveChat("未婚状态", ChatType.System);
                            return;
                        }

                        if (Dead)
                        {
                            ReceiveChat("死亡状态不能召唤", ChatType.System);
                            return;
                        }

                        if (CurrentMap.Info.NoRecall)
                        {
                            ReceiveChat("地图禁用召唤", ChatType.System);
                            return;
                        }

                        if (Info.Equipment[(int)EquipmentSlot.左戒指] == null)
                        {
                            ReceiveChat("需要戴结婚戒指才能召唤", ChatType.System);
                            return;
                        }


                        if (Info.Equipment[(int)EquipmentSlot.左戒指].WeddingRing == Info.Married)
                        {
                            CharacterInfo Lover = Envir.GetCharacterInfo(Info.Married);

                            if (Lover == null) return;

                            player = Envir.GetPlayer(Lover.Name);

                            if (!Settings.WeddingRingRecall)
                            {
                                ReceiveChat($"无法通过结婚戒指跨图传送", ChatType.System);
                                return;
                            }

                            if (player == null)
                            {
                                ReceiveChat((string.Format("{0} 未在线", Lover.Name)), ChatType.System);
                                return;
                            }

                            if (player.Dead)
                            {
                                ReceiveChat("角色死亡不能召唤", ChatType.System);
                                return;
                            }

                            if (player.Info.Equipment[(int)EquipmentSlot.左戒指] == null)
                            {
                                player.ReceiveChat((string.Format("需要戴结婚戒指才能召唤", Lover.Name)), ChatType.System);
                                ReceiveChat((string.Format("{0} 对方未佩戴结婚戒指", Lover.Name)), ChatType.System);
                                return;
                            }

                            if (player.Info.Equipment[(int)EquipmentSlot.左戒指].WeddingRing != player.Info.Married)
                            {
                                player.ReceiveChat((string.Format("左戒指位佩戴结婚戒指使用召唤", Lover.Name)), ChatType.System);
                                ReceiveChat((string.Format("{0} 对方未戴结婚戒指", Lover.Name)), ChatType.System);
                                return;
                            }

                            if (!player.AllowLoverRecall)
                            {
                                player.ReceiveChat("来自心心相映的召唤，请在夫妻栏(L)中开启允许召唤",
                                        ChatType.System);
                                ReceiveChat((string.Format("{0} 拒绝你的召唤", player.Name)), ChatType.System);
                                return;
                            }

                            if ((Envir.Time < LastRecallTime) && (Envir.Time < player.LastRecallTime))
                            {
                                ReceiveChat(string.Format("夫妻传送计时 {0} 秒", (LastRecallTime - Envir.Time) / 1000), ChatType.System);
                                return;
                            }

                            LastRecallTime = Envir.Time + 60000;
                            player.LastRecallTime = Envir.Time + 60000;

                            if (!player.Teleport(CurrentMap, Front))
                                player.Teleport(CurrentMap, CurrentLocation);
                        }
                        else
                        {
                            ReceiveChat("未戴结婚戒指无法召唤", ChatType.System);
                            return;
                        }
                        break;
                    case "TIME":
                        ReceiveChat(string.Format("现在时间: {0}", Envir.Now.ToString("hh:mm tt")), ChatType.System);
                        break;

                    case "ROLL":
                        int diceNum = Envir.Random.Next(5) + 1;

                        if (GroupMembers == null) { return; }

                        for (int i = 0; i < GroupMembers.Count; i++)
                        {
                            PlayerObject playerSend = GroupMembers[i];
                            playerSend.ReceiveChat(string.Format("{0} 掷出了 {1}", Name, diceNum), ChatType.Group);
                        }
                        break;

                    case "MAP":
                        var mapName = CurrentMap.Info.FileName;
                        var mapTitle = CurrentMap.Info.Title;
                        ReceiveChat((string.Format("当前位于 {0} 地图 ID: {1}", mapTitle, mapName)), ChatType.System);
                        break;

                    case "BACKUPPLAYER":
                        {
                            if (!IsGM || parts.Length < 2) return;

                            var info = Envir.GetCharacterInfo(parts[1]);

                            if (info == null)
                            {
                                ReceiveChat(string.Format("未找到玩家 {0}", parts[1]), ChatType.System);
                                return;
                            }

                            Envir.SaveArchivedCharacter(info);

                            ReceiveChat(string.Format("玩家 {0} 已被备份", info.Name), ChatType.System);
                            MessageQueue.Enqueue(string.Format("玩家 {0} 已被 {1} 备份", info.Name, Name));
                        }
                        break;

                    case "ARCHIVEPLAYER":
                        {
                            if (!IsGM || parts.Length < 2) return;

                            data = Envir.GetCharacterInfo(parts[1]);

                            if (data == null)
                            {
                                ReceiveChat(string.Format("未找到玩家 {0}", parts[1]), ChatType.System);
                                return;
                            }

                            if (data == Info)
                            {
                                ReceiveChat("不能归档当前玩家", ChatType.System);
                                return;
                            }

                            var account = Envir.GetAccountByCharacter(parts[1]);

                            if (account == null)
                            {
                                ReceiveChat(string.Format("未在任何账户中找到玩家 {0}", parts[1]), ChatType.System);
                                return;
                            }

                            Envir.SaveArchivedCharacter(data);

                            Envir.CharacterList.Remove(data);
                            account.Characters.Remove(data);

                            ReceiveChat(string.Format("玩家 {0} 已被归档", data.Name), ChatType.System);
                            MessageQueue.Enqueue(string.Format("玩家 {0} 已被 {1} 归档", data.Name, Name));
                        }
                        break;

                    case "LOADPLAYER":
                        {
                            if (!IsGM) return;

                            if (parts.Length < 2) return;

                            var bak = Envir.GetArchivedCharacter(parts[1]);

                            if (bak == null)
                            {
                                ReceiveChat(string.Format("玩家 {0} 无法加载-请尝试指定完整的存档文件名", parts[1]), ChatType.System);
                                return;
                            }

                            var info = Envir.GetCharacterInfo(bak.Name);

                            if (info == null)
                            {
                                ReceiveChat(string.Format("未找到玩家 {0}", parts[1]), ChatType.System);
                                return;
                            }

                            if (info.Index != bak.Index)
                            {
                                ReceiveChat("由于ID不匹配，无法加载该玩家", ChatType.System);
                                return;
                            }

                            info = bak;

                            ReceiveChat(string.Format("玩家 {0} 已被加载", info.Name), ChatType.System);
                            MessageQueue.Enqueue(string.Format("玩家 {0} 已被 {1} 加载", info.Name, Name));
                        }
                        break;

                    case "RESTOREPLAYER":
                        {
                            if (!IsGM || parts.Length < 2) return;

                            AccountInfo account = null;

                            if (parts.Length > 2)
                            {
                                if (!Envir.AccountExists(parts[2]))
                                {
                                    ReceiveChat(string.Format(" 未找到 {0} 这个账户", parts[2]), ChatType.System);
                                    return;
                                }

                                account = Envir.GetAccount(parts[2]);

                                if (account.Characters.Count >= Globals.MaxCharacterCount)
                                {
                                    ReceiveChat(string.Format("账户 {0} 已经有 {1} ", parts[2], Globals.MaxCharacterCount), ChatType.System);
                                    return;
                                }
                            }

                            data = Envir.GetCharacterInfo(parts[1]);

                            if (data == null)
                            {
                                if (account != null)
                                {
                                    data = Envir.GetArchivedCharacter(parts[1]);

                                    if (data == null)
                                    {
                                        ReceiveChat(string.Format("玩家 {0} 无法恢复-请尝试指定完整的存档文件名", parts[1]), ChatType.System);
                                        return;
                                    }

                                    data.AccountInfo = account;

                                    account.Characters.Add(data);
                                    Envir.CharacterList.Add(data);

                                    data.Deleted = false;
                                    data.DeleteDate = DateTime.MinValue;

                                    data.LastLoginDate = Envir.Now;
                                }
                                else
                                {
                                    ReceiveChat(string.Format("未找到玩家 {0}", parts[1]), ChatType.System);
                                    return;
                                }
                            }
                            else
                            {
                                if (!data.Deleted) return;
                                data.Deleted = false;
                                data.DeleteDate = DateTime.MinValue;
                            }

                            ReceiveChat(string.Format("玩家 {0} 已被恢复", data.Name), ChatType.System);
                            MessageQueue.Enqueue(string.Format("玩家 {0} 已被 {1} 恢复", data.Name, Name));
                        }
                        break;

                    case "传送":
                        if (!IsGM && !SpecialMode.HasFlag(SpecialItemMode.Teleport) && !Settings.TestServer) return;
                        if (!IsGM && CurrentMap.Info.NoPosition)
                        {
                            ReceiveChat(("地图禁用传送戒指"), ChatType.System);
                            return;
                        }
                        if (Dead)
                        {
                            ReceiveChat("死亡状态无法使用传送戒指", ChatType.System);
                            return;
                        }
                        if (Envir.Time < LastTeleportTime)
                        {
                            ReceiveChat(string.Format("传送冷却时间 {0} 秒", (LastTeleportTime - Envir.Time) / 1000), ChatType.System);
                            return;
                        }

                        int x, y;

                        if (parts.Length <= 2 || !int.TryParse(parts[1], out x) || !int.TryParse(parts[2], out y))
                        {
                            if (!IsGM)
                                LastTeleportTime = Envir.Time + 10000;
                            TeleportRandom(200, 0);
                            return;
                        }
                        if (!IsGM)
                            LastTeleportTime = Envir.Time + 10000;
                        Teleport(CurrentMap, new Point(x, y));
                        break;

                    case "MAPMOVE":
                        if ((!IsGM && !Settings.TestServer) || parts.Length < 2) return;
                        var instanceID = 1; x = 0; y = 0;

                        if (parts.Length == 3 || parts.Length == 5)
                            int.TryParse(parts[2], out instanceID);

                        if (instanceID < 1) instanceID = 1;

                        var map = Envir.GetMapByNameAndInstance(parts[1], instanceID);
                        if (map == null)
                        {
                            ReceiveChat((string.Format("地图 {0}:[{1}] 不存在", parts[1], instanceID)), ChatType.System);
                            return;
                        }

                        if (parts.Length == 4 || parts.Length == 5)
                        {
                            int.TryParse(parts[parts.Length - 2], out x);
                            int.TryParse(parts[parts.Length - 1], out y);
                        }

                        switch (parts.Length)
                        {
                            case 2:
                                ReceiveChat(TeleportRandom(200, 0, map) ? (string.Format("传送地图 {0}", map.Info.FileName)) :
                                    (string.Format("地图传送失败 {0}", map.Info.FileName)), ChatType.System);
                                break;
                            case 3:
                                ReceiveChat(TeleportRandom(200, 0, map) ? (string.Format("角色传送地图 {0}:[{1}]", map.Info.FileName, instanceID)) :
                                    (string.Format("地图角色传送失败 {0}:[{1}]", map.Info.FileName, instanceID)), ChatType.System);
                                break;
                            case 4:
                                ReceiveChat(Teleport(map, new Point(x, y)) ? (string.Format("坐标传送地图 {0} 坐标 {1}:{2}", map.Info.FileName, x, y)) :
                                    (string.Format("地图坐标传送失败 {0} at {1}:{2}", map.Info.FileName, x, y)), ChatType.System);
                                break;
                            case 5:
                                ReceiveChat(Teleport(map, new Point(x, y)) ? (string.Format("角色坐标传送地图 {0}:[{1}] 坐标 {2}:{3}", map.Info.FileName, instanceID, x, y)) :
                                    (string.Format("地图角色坐标传送失败 {0}:[{1}] at {2}:{3}", map.Info.FileName, instanceID, x, y)), ChatType.System);
                                break;
                        }
                        break;

                    case "GOTO":
                        if (!IsGM) return;

                        if (parts.Length < 2) return;
                        player = Envir.GetPlayer(parts[1]);

                        if (player == null) return;

                        Teleport(player.CurrentMap, player.CurrentLocation);
                        break;

                    case "MOB":
                        if (!IsGM && !Settings.TestServer) return;
                        if (parts.Length < 2)
                        {
                            ReceiveChat("刷怪命令参数不正确", ChatType.System);
                            return;
                        }

                        MonsterInfo mInfo = null;
                        int monsterIndex = 0;

                        if (Int32.TryParse(parts[1], out monsterIndex))
                        {
                            mInfo = Envir.GetMonsterInfo(monsterIndex, false);
                        }
                        else
                        {
                            mInfo = Envir.GetMonsterInfo(parts[1]);
                        }

                        if (mInfo == null)
                        {
                            ReceiveChat((string.Format("怪物 {0} 不存在", parts[1])), ChatType.System);
                            return;
                        }

                        if (conquestAIs.Contains(mInfo.AI))
                        {
                            ReceiveChat($"此命令不能生成攻城类怪物: {mInfo.Name}", ChatType.System);
                            return;
                        }

                        uint count = 1;
                        if (parts.Length >= 3 && IsGM)
                            if (!uint.TryParse(parts[2], out count)) count = 1;
                        int spread = 0;
                        if (parts.Length >= 4)
                            int.TryParse(parts[3], out spread);

                        for (int i = 0; i < count; i++)
                        {
                            MonsterObject monster = MonsterObject.GetMonster(mInfo);
                            if (monster == null)
                            {
                                return;
                            }

                            if (monster is IntelligentCreatureObject)
                            {
                                ReceiveChat("此命令不能生成灵物类怪物", ChatType.System);
                                return;
                            }

                            monster.GMMade = true;

                            if (spread == 0)
                                monster.Spawn(CurrentMap, Front);
                            else
                                for (int _ = 0; _ < 20; _++)
                                    if (monster.Spawn(CurrentMap, CurrentLocation.Add(Envir.Random.Next(-spread, spread + 1), Envir.Random.Next(-spread, spread + 1))))
                                        break;
                        }

                        ReceiveChat((string.Format("怪物 {0} x{1} 已生成", mInfo.Name, count)), ChatType.System);
                        break;

                    case "RECALLMOB":
                        if ((!IsGM && !Settings.TestServer) || parts.Length < 2) return;

                        MonsterInfo mInfo2 = null;
                        int monsterIndex2 = 0;

                        if (Int32.TryParse(parts[1], out monsterIndex2))
                        {
                            mInfo2 = Envir.GetMonsterInfo(monsterIndex2, false);
                        }
                        else
                        {
                            mInfo2 = Envir.GetMonsterInfo(parts[1]);
                        }

                        if (mInfo2 == null) return;

                        count = 1;
                        byte petlevel = 0;

                        if (parts.Length > 2)
                            if (!uint.TryParse(parts[2], out count) || count > 50) count = 1;

                        if (parts.Length > 3)
                            if (!byte.TryParse(parts[3], out petlevel) || petlevel > 7) petlevel = 0;

                        if (!IsGM && (Pets.Count(t => !t.Dead && t.Race != ObjectType.Creature) >= Globals.MaxPets)) return;

                        for (int i = 0; i < count; i++)
                        {
                            MonsterObject monster = MonsterObject.GetMonster(mInfo2);

                            if (monster == null) return;

                            if (conquestAIs.Contains(monster.Info.AI))
                            {
                                ReceiveChat($"无法生成攻城类怪物: {monster.Name}", ChatType.System);
                                return;
                            }
                            else if (monster is IntelligentCreatureObject)
                            {
                                ReceiveChat($"无法生成灵物类怪物", ChatType.System);
                                return;
                            }

                            monster.PetLevel = petlevel;
                            monster.Master = this;
                            monster.MaxPetLevel = 7;
                            monster.Direction = Direction;
                            monster.ActionTime = Envir.Time + 1000;
                            monster.Spawn(CurrentMap, Front);
                            Pets.Add(monster);
                        }

                        ReceiveChat((string.Format("宠物 {0} x{1} 已召唤", mInfo2.Name, count)), ChatType.System);
                        break;

                    case "RELOADDROPS":
                        if (!IsGM) return;

                        Envir.ReloadDrops();

                        ReceiveChat("掉落几率重新加载", ChatType.Hint);
                        break;

                    case "RELOADNPCS":
                        if (!IsGM) return;

                        Envir.ReloadNPCs();

                        ReceiveChat("NPC脚本已重新加载", ChatType.Hint);
                        break;

                    case "CLEARIPBLOCKS":
                        if (!IsGM) return;

                        Envir.IPBlocks.Clear();
                        break;

                    case "GIVEGOLD":
                        if ((!IsGM && !Settings.TestServer) || parts.Length < 2) return;

                        player = this;

                        if (parts.Length > 2)
                        {
                            if (!IsGM) return;

                            if (!uint.TryParse(parts[2], out count)) return;
                            player = Envir.GetPlayer(parts[1]);

                            if (player == null)
                            {
                                ReceiveChat(string.Format("玩家 {0} 未在线", parts[1]), ChatType.System);
                                return;
                            }
                        }

                        else if (!uint.TryParse(parts[1], out count)) return;

                        if (count + player.Account.Gold >= uint.MaxValue)
                            count = uint.MaxValue - player.Account.Gold;

                        player.GainGold(count);
                        
                        string goldMsg = $"游戏管理员:{Name} 给予玩家:{player.Name} {count}金币";
                        MessageQueue.Enqueue(goldMsg);
                        Helpers.ChatSystem.SystemMessage(chatMessage: goldMsg);

                        break;

                    case "GIVEPEARLS":
                        if ((!IsGM && !Settings.TestServer) || parts.Length < 2) return;

                        player = this;

                        if (parts.Length > 2)
                        {
                            if (!IsGM) return;

                            if (!uint.TryParse(parts[2], out count)) return;
                            player = Envir.GetPlayer(parts[1]);

                            if (player == null)
                            {
                                ReceiveChat(string.Format("玩家 {0} 未在线", parts[1]), ChatType.System);
                                return;
                            }
                        }

                        else if (!uint.TryParse(parts[1], out count)) return;

                        if (count + player.Info.PearlCount >= int.MaxValue)
                            count = (uint)(int.MaxValue - player.Info.PearlCount);

                        player.IntelligentCreatureGainPearls((int)count);

                        string pearlMsg = count == 1 ? $"游戏管理员:{Name} 给予玩家:{player.Name}一颗珍珠"
                                                     : $"游戏管理员:{Name} 给予玩家:{player.Name} {count}颗珍珠";

                        MessageQueue.Enqueue(pearlMsg);
                        Helpers.ChatSystem.SystemMessage(chatMessage: pearlMsg);

                        break;
                    case "GIVECREDIT":
                        if ((!IsGM && !Settings.TestServer) || parts.Length < 2) return;

                        player = this;

                        if (parts.Length > 2)
                        {
                            if (!IsGM) return;

                            if (!uint.TryParse(parts[2], out count)) return;
                            player = Envir.GetPlayer(parts[1]);

                            if (player == null)
                            {
                                ReceiveChat(string.Format("玩家 {0} 未在线", parts[1]), ChatType.System);
                                return;
                            }
                        }

                        else if (!uint.TryParse(parts[1], out count)) return;

                        if (count + player.Account.Credit >= uint.MaxValue)
                            count = uint.MaxValue - player.Account.Credit;

                        player.GainCredit(count);

                        string creditMsg = $"游戏管理员:{Name} 给予玩家:{player.Name} {count} 信用币";

                        MessageQueue.Enqueue(string.Format("玩家 {0} 已获得 {1} 信用币", player.Name, count));
                        Helpers.ChatSystem.SystemMessage(chatMessage: creditMsg);

                        break;
                    case "GIVESKILL":
                        if ((!IsGM && !Settings.TestServer) || parts.Length < 3) return;

                        byte spellLevel = 0;

                        player = this;
                        Spell skill;

                        if (!Enum.TryParse(parts.Length > 3 ? parts[2] : parts[1], true, out skill) || !Enum.IsDefined(skill)) return;

                        if (skill == Spell.None) return;

                        spellLevel = byte.TryParse(parts.Length > 3 ? parts[3] : parts[2], out spellLevel) ? Math.Min((byte)3, spellLevel) : (byte)0;

                        if (parts.Length > 3)
                        {
                            if (!IsGM) return;

                            player = Envir.GetPlayer(parts[1]);

                            if (player == null)
                            {
                                ReceiveChat(string.Format("未找到玩家 {0}", parts[1]), ChatType.System);
                                return;
                            }
                        }

                        var magic = new UserMagic(skill) { Level = spellLevel };

                        if (player.Info.Magics.Any(e => e.Spell == skill))
                        {
                            player.Info.Magics.FirstOrDefault(e => e.Spell == skill).Level = spellLevel;

                            string skillChangeMsg = $"{player.Name} 的技能 {skill.ToString()} 被管理员 {Name} 调整为 {spellLevel}";

                            player.ReceiveChat(string.Format(" {0} 技能等级调整为 {1}", skill.ToString(), spellLevel), ChatType.Hint);
                            Helpers.ChatSystem.SystemMessage(chatMessage: skillChangeMsg);

                            return;
                        }
                        else
                        {
                            player.ReceiveChat(string.Format("{0} 的技能等级: {1}", skill.ToString(), spellLevel), ChatType.Hint);

                            if (player != this)
                            {
                                ReceiveChat(string.Format("{0} 技能等级由 {1} 提升到 {2}", player.Name, skill.ToString(), spellLevel), ChatType.Hint);
                            }

                            string skillLearnedMg = $"{player.Name} 已有技能 {skill.ToString()} ，被管理员 {Name} 调整为 {spellLevel}";
                            Helpers.ChatSystem.SystemMessage(chatMessage: skillLearnedMg);

                            player.Info.Magics.Add(magic);
                        }

                        player.SendMagicInfo(magic);
                        player.RefreshStats();
                        break;

                    case "探测":
                        if (!IsGM && !SpecialMode.HasFlag(SpecialItemMode.Probe)) return;

                        if (Envir.Time < LastProbeTime)
                        {
                            ReceiveChat(string.Format("再次使用间隔 {0} 秒", (LastProbeTime - Envir.Time) / 1000), ChatType.System);
                            return;
                        }

                        if (parts.Length < 2) return;
                        player = Envir.GetPlayer(parts[1]);

                        if (player == null)
                        {
                            ReceiveChat(parts[1] + " 未在线", ChatType.System);
                            return;
                        }
                        if (player.CurrentMap == null) return;
                        if (!IsGM)
                            LastProbeTime = Envir.Time + 180000;
                        ReceiveChat((string.Format("{0} 位于 {1} ({2},{3})", player.Name, player.CurrentMap.Info.Title, player.CurrentLocation.X, player.CurrentLocation.Y)), ChatType.System);
                        break;

                    case "退出行会":
                        if (MyGuild == null) return;
                        if (MyGuildRank == null) return;
                        if(MyGuild.IsAtWar())
                        {
                            ReceiveChat("公会战期间不能离开行会", ChatType.System);
                            return;
                        }
                        if (MyGuild.Name == Settings.NewbieGuild && Settings.NewbieGuildBuffEnabled == true) RemoveBuff(BuffType.新人特效);
                        if (HasBuff(BuffType.公会特效)) RemoveBuff(BuffType.公会特效);
                        MyGuild.DeleteMember(this, Name);
                        break;

                    case "CREATEGUILD":

                        if ((!IsGM && !Settings.TestServer) || parts.Length < 2) return;

                        player = parts.Length < 3 ? this : Envir.GetPlayer(parts[1]);

                        if (player == null)
                        {
                            ReceiveChat(string.Format("玩家 {0} 未在线", parts[1]), ChatType.System);
                            return;
                        }

                        if (player.MyGuild != null)
                        {
                            ReceiveChat(string.Format("玩家 {0} 已经加入行会", player.Name), ChatType.System);
                            return;
                        }

                        String gName = parts.Length < 3 ? parts[1] : parts[2];
                        if ((gName.Length < 3) || (gName.Length > 20))
                        {
                            ReceiveChat("行会名称限制为 3-20 字符", ChatType.System);
                            return;
                        }

                        GuildObject guild = Envir.GetGuild(gName);
                        if (guild != null)
                        {
                            ReceiveChat(string.Format("行会 {0} 已经存在", gName), ChatType.System);
                            return;
                        }

                        player.CanCreateGuild = true;
                        if (player.CreateGuild(gName))
                        {
                            ReceiveChat(string.Format("行会创建成功 {0}", gName), ChatType.System);
                        }
                        else
                        {
                            ReceiveChat("创建行会失败", ChatType.System);
                        }

                        player.CanCreateGuild = false;
                        break;

                    case "允许交易":
                        AllowTrade = !AllowTrade;

                        if (AllowTrade)
                            ReceiveChat("允许交易", ChatType.System);
                        else
                            ReceiveChat("关闭交易", ChatType.System);
                        break;

                    case "TRIGGER":
                        if (!IsGM) return;
                        if (parts.Length < 2) return;

                        if (parts.Length >= 3)
                        {
                            player = Envir.GetPlayer(parts[2]);

                            if (player == null)
                            {
                                ReceiveChat(string.Format("玩家 {0} 未发现", parts[2]), ChatType.System);
                                return;
                            }

                            player.CallDefaultNPC(DefaultNPCType.Trigger, parts[1]);
                            return;
                        }

                        foreach (var pl in Envir.Players)
                        {
                            pl.CallDefaultNPC(DefaultNPCType.Trigger, parts[1]);
                        }

                        break;

                    case "RIDE":
                        ToggleRide();

                        if (HasHero && HeroSpawned && Hero.RidingMount != RidingMount)
                            Hero.ToggleRide();

                        ChatTime = 0;
                        break;
                    case "SETFLAG":
                        if (!IsGM && !Settings.TestServer) return;

                        if (parts.Length < 2) return;

                        int tempInt = 0;

                        if (!int.TryParse(parts[1], out tempInt)) return;

                        if (tempInt > Info.Flags.Length - 1) return;

                        Info.Flags[tempInt] = !Info.Flags[tempInt];

                        for (int f = CurrentMap.NPCs.Count - 1; f >= 0; f--)
                        {
                            if (Functions.InRange(CurrentMap.NPCs[f].CurrentLocation, CurrentLocation, Globals.DataRange))
                                CurrentMap.NPCs[f].CheckVisible(this);
                        }

                        break;

                    case "LISTFLAGS":
                        if (!IsGM && !Settings.TestServer) return;

                        for (int i = 0; i < Info.Flags.Length; i++)
                        {
                            if (Info.Flags[i] == false) continue;

                            ReceiveChat("Flag " + i, ChatType.Hint);
                        }
                        break;

                    case "CLEARFLAGS":
                        if (!IsGM && !Settings.TestServer) return;

                        player = parts.Length > 1 && IsGM ? Envir.GetPlayer(parts[1]) : this;

                        if (player == null)
                        {
                            ReceiveChat(parts[1] + " 未在线", ChatType.System);
                            return;
                        }

                        for (int i = 0; i < player.Info.Flags.Length; i++)
                        {
                            player.Info.Flags[i] = false;
                        }
                        break;
                    case "CLEARMOB":
                        if (!IsGM) return;

                        if (parts.Length > 1)
                        {
                            map = Envir.GetMapByNameAndInstance(parts[1]);

                            if (map == null) return;

                        }
                        else
                        {
                            map = CurrentMap;
                        }

                        foreach (var cell in map.Cells)
                        {
                            if (cell == null || cell.Objects == null) continue;

                            int obCount = cell.Objects.Count();

                            for (int m = 0; m < obCount; m++)
                            {
                                MapObject ob = cell.Objects[m];

                                if (ob.Race != ObjectType.Monster) continue;
                                if (ob.Dead) continue;
                                ob.Die();
                            }
                        }

                        break;

                    case "CHANGECLASS": //@changeclass [Player] [Class]
                        if (!IsGM && !Settings.TestServer) return;

                        data = parts.Length <= 2 || !IsGM ? Info : Envir.GetCharacterInfo(parts[1]);

                        if (data == null) return;

                        MirClass mirClass;

                        if (!Enum.TryParse(parts[parts.Length - 1], true, out mirClass) || data.Class == mirClass) return;

                        data.Class = mirClass;

                        ReceiveChat(string.Format("角色 {0} 职业已更改为 {1}", data.Name, data.Class), ChatType.System);
                        MessageQueue.Enqueue(string.Format("游戏管理员:{2} 将玩家 {0} 职业更改为 {1}", data.Name, data.Class, Name));

                        Helpers.ChatSystem.SystemMessage(chatMessage: $"游戏管理员:{Name} 将玩家:{data.Player.Name} 职业变更为 {data.Class.ToString()}");

                        if (data.Player != null)
                        data.Player.Connection.LogOut();
                        break;

                    case "DIE":
                        LastHitter = null;
                        Die();
                        break;
                    case "HAIR":
                        if (!IsGM && !Settings.TestServer) return;

                        if (parts.Length < 2)
                        {
                            Info.Hair = (byte)Envir.Random.Next(0, 9);
                        }
                        else
                        {
                            byte tempByte = 0;

                            byte.TryParse(parts[1], out tempByte);

                            Info.Hair = tempByte;
                        }
                        break;

                    case "DECO":
                        if ((!IsGM && !Settings.TestServer) || parts.Length < 2) return;

                        int.TryParse(parts[1], out tempInt);

                        DecoObject decoOb = new DecoObject
                        {
                            Image = tempInt,
                            CurrentMap = CurrentMap,
                            CurrentLocation = CurrentLocation,
                        };

                        CurrentMap.AddObject(decoOb);
                        decoOb.Spawned();

                        Enqueue(decoOb.GetInfo());

                        break;

                    case "ADJUSTPKPOINT":
                        if ((!IsGM && !Settings.TestServer) || parts.Length < 2) return;

                        if (parts.Length > 2)
                        {
                            if (!IsGM) return;

                            player = Envir.GetPlayer(parts[1]);

                            if (player == null) return;


                            int.TryParse(parts[2], out tempInt);
                        }
                        else
                        {
                            player = this;
                            int.TryParse(parts[1], out tempInt);
                        }

                        player.PKPoints = tempInt;

                        break;

                    case "AWAKENING":
                        {
                            if ((!IsGM && !Settings.TestServer) || parts.Length < 3) return;

                            ItemType type;

                            if (!Enum.TryParse(parts[1], true, out type)) return;

                            AwakeType awakeType;

                            if (!Enum.TryParse(parts[2], true, out awakeType)) return;

                            foreach (UserItem temp in Info.Equipment)
                            {
                                if (temp == null) continue;

                                ItemInfo realItem = Functions.GetRealItem(temp.Info, Info.Level, Info.Class, Envir.ItemInfoList);

                                if (realItem.Type == type)
                                {
                                    Awake awake = temp.Awake;
                                    bool[] isHit;
                                    int result = awake.UpgradeAwake(temp, awakeType, out isHit);
                                    switch (result)
                                    {
                                        case -1:
                                            ReceiveChat(string.Format("{0} : 未达到觉醒所需的要求", temp.FriendlyName), ChatType.System);
                                            break;
                                        case 0:
                                            ReceiveChat(string.Format("{0} : 升级失败", temp.FriendlyName), ChatType.System);
                                            break;
                                        case 1:
                                            ReceiveChat(string.Format("{0} : 觉醒等级 {1}, value {2}~{3}", temp.FriendlyName, awake.GetAwakeLevel(), awake.GetAwakeValue(), awake.GetAwakeValue()), ChatType.System);
                                            p = new S.RefreshItem { Item = temp };
                                            Enqueue(p);
                                            break;
                                        default:
                                            break;
                                    }
                                }
                            }
                        }
                        break;
                    case "REMOVEAWAKENING":
                        {
                            if ((!IsGM && !Settings.TestServer) || parts.Length < 2) return;

                            ItemType type;

                            if (!Enum.TryParse(parts[1], true, out type)) return;

                            foreach (UserItem temp in Info.Equipment)
                            {
                                if (temp == null) continue;

                                ItemInfo realItem = Functions.GetRealItem(temp.Info, Info.Level, Info.Class, Envir.ItemInfoList);

                                if (realItem.Type == type)
                                {
                                    Awake awake = temp.Awake;
                                    int result = awake.RemoveAwake();
                                    switch (result)
                                    {
                                        case 0:
                                            ReceiveChat(string.Format("{0} : 觉醒等级恢复为 0 级", temp.FriendlyName), ChatType.System);
                                            break;
                                        case 1:
                                            ReceiveChat(string.Format("{0} : 觉醒等级降为 {1}", temp.FriendlyName, temp.Awake.GetAwakeLevel()), ChatType.System);
                                            p = new S.RefreshItem { Item = temp };
                                            Enqueue(p);
                                            break;
                                        default:
                                            break;
                                    }
                                }
                            }
                        }
                        break;

                    case "STARTWAR":
                        if (!IsGM) return;
                        if (parts.Length < 2) return;

                        GuildObject enemyGuild = Envir.GetGuild(parts[1]);

                        if (MyGuild == null)
                        {
                            ReceiveChat(GameLanguage.NotInGuild, ChatType.System);
                        }

                        if (MyGuild.Ranks[0] != MyGuildRank)
                        {
                            ReceiveChat("行会战必须由会长发起", ChatType.System);
                            return;
                        }

                        if (enemyGuild == null)
                        {
                            ReceiveChat(string.Format("未找到行会： {0}", parts[1]), ChatType.System);
                            return;
                        }

                        if (MyGuild == enemyGuild)
                        {
                            ReceiveChat("不能向自己的行会开战", ChatType.System);
                            return;
                        }

                        if (enemyGuild.Name == Settings.NewbieGuild)
                        {
                            ReceiveChat("不能向新手玩家公会宣战", ChatType.System);
                            return;
                        }

                        if (MyGuild.WarringGuilds.Contains(enemyGuild))
                        {
                            ReceiveChat("已经和这个行会交战", ChatType.System);
                            return;
                        }

                        if (MyGuild.GoToWar(enemyGuild))
                        {
                            ReceiveChat(string.Format("你发起了行会战 {0}", parts[1]), ChatType.System);
                            enemyGuild.SendMessage(string.Format("{0} 发起了行会战", MyGuild.Name), ChatType.System);
                        }
                        break;
                    case "ADDINVENTORY":
                        {
                            int openLevel = (int)((Info.Inventory.Length - 46) / 4);
                            uint openGold = (uint)(1000000 + openLevel * 1000000);
                            if (Account.Gold >= openGold)
                            {
                                Account.Gold -= openGold;
                                Enqueue(new S.LoseGold { Gold = openGold });
                                Enqueue(new S.ResizeInventory { Size = Info.ResizeInventory() });
                                ReceiveChat(GameLanguage.InventoryIncreased, ChatType.System);
                            }
                            else
                            {
                                ReceiveChat(GameLanguage.LowGold, ChatType.System);
                            }
                            ChatTime = 0;
                        }
                        break;

                    case "ADDSTORAGE":
                        {
                            TimeSpan addedTime = new TimeSpan(10, 0, 0, 0);
                            uint cost = 1000000;

                            if (Account.Gold >= cost)
                            {
                                Account.Gold -= cost;
                                Account.HasExpandedStorage = true;

                                if (Account.ExpandedStorageExpiryDate > Envir.Now)
                                {
                                    Account.ExpandedStorageExpiryDate = Account.ExpandedStorageExpiryDate + addedTime;
                                    ReceiveChat(GameLanguage.ExpandedStorageExpiresOn + Account.ExpandedStorageExpiryDate.ToString(), ChatType.System);
                                }
                                else
                                {
                                    Account.ExpandedStorageExpiryDate = Envir.Now + addedTime;
                                    ReceiveChat(GameLanguage.ExpandedStorageExpiresOn + Account.ExpandedStorageExpiryDate.ToString(), ChatType.System);
                                }

                                Enqueue(new S.LoseGold { Gold = cost });
                                Enqueue(new S.ResizeStorage { Size = Account.ExpandStorage(), HasExpandedStorage = Account.HasExpandedStorage, ExpiryTime = Account.ExpandedStorageExpiryDate });
                            }
                            else
                            {
                                ReceiveChat(GameLanguage.LowGold, ChatType.System);
                            }
                            ChatTime = 0;
                        }
                        break;

                    case "SUMMONHERO":
                        {
                            if (!HasHero) return;

                            if (!HeroSpawned)
                            {
                                SummonHero();
                                switch (Class)
                                {
                                    case MirClass.战士:
                                        AddBuff(BuffType.英雄灵气, this, 0, new Stats { [Stat.HP] = 300, [Stat.物品掉落数率] = 5 });
                                        break;
                                    case MirClass.法师:
                                        AddBuff(BuffType.英雄灵气, this, 0, new Stats { [Stat.MP] = 300, [Stat.经验增长数率] = 5 });
                                        break;
                                    case MirClass.道士:
                                        AddBuff(BuffType.英雄灵气, this, 0, new Stats { [Stat.生命恢复] = 2, [Stat.攻击增伤] = 3 });
                                        break;
                                    case MirClass.刺客:
                                        AddBuff(BuffType.英雄灵气, this, 0, new Stats { [Stat.法力恢复] = 2, [Stat.准确] = 3 });
                                        break;
                                    case MirClass.弓箭:
                                        AddBuff(BuffType.英雄灵气, this, 0, new Stats { [Stat.敏捷] = 2, [Stat.最大物理攻击数率] = 2, [Stat.最大魔法攻击数率] = 1 });
                                        break;
                                    default:
                                        break;
                                }
                            }
                            else
                            {
                                DespawnHero();
                                Info.HeroSpawned = false;
                                RemoveBuff(BuffType.英雄灵气);
                            }
                        }
                        break;

                    case "ALLOWOBSERVE":
                        AllowObserve = !AllowObserve;
                        Enqueue(new S.AllowObserve { Allow = AllowObserve });
                        break;

                    case "INFO":
                        {
                            if (!IsGM && !Settings.TestServer) return;

                            MapObject ob = null;

                            if (parts.Length < 2)
                            {
                                Point target = Functions.PointMove(CurrentLocation, Direction, 1);
                                if (!CurrentMap.ValidPoint(target)) return;
                                Cell cell = CurrentMap.GetCell(target);

                                if (cell.Objects == null || cell.Objects.Count < 1) return;

                                ob = cell.Objects[0];
                            }
                            else
                            {
                                ob = Envir.GetPlayer(parts[1]);
                            }

                            if (ob == null) return;

                            switch (ob.Race)
                            {
                                case ObjectType.Player:
                                    PlayerObject plOb = (PlayerObject)ob;
                                    ReceiveChat("--玩家信息--", ChatType.System2);
                                    ReceiveChat(string.Format("名称 : {0}, 等级 : {1}, X : {2}, Y : {3}", plOb.Name, plOb.Level, plOb.CurrentLocation.X, plOb.CurrentLocation.Y), ChatType.System2);
                                    break;
                                case ObjectType.Monster:
                                    MonsterObject monOb = (MonsterObject)ob;
                                    ReceiveChat("--怪物信息--", ChatType.System2);
                                    ReceiveChat(string.Format("ID : {0}, 怪物名 : {1}", monOb.Info.Index, monOb.Name), ChatType.System2);
                                    ReceiveChat(string.Format("Level : {0}, X : {1}, Y : {2}, Dir: {3}", monOb.Level, monOb.CurrentLocation.X, monOb.CurrentLocation.Y, monOb.Direction), ChatType.System2);
                                    ReceiveChat(string.Format("HP : {0}, MinDC : {1}, MaxDC : {2}", monOb.Info.Stats[Stat.HP], monOb.Stats[Stat.MinDC], monOb.Stats[Stat.MaxDC]), ChatType.System2);
                                    break;
                                case ObjectType.Merchant:
                                    NPCObject npcOb = (NPCObject)ob;
                                    ReceiveChat("--NPC信息--", ChatType.System2);
                                    ReceiveChat(string.Format("ID : {0}, NPC名 : {1}", npcOb.Info.Index, npcOb.Name), ChatType.System2);
                                    ReceiveChat(string.Format("X : {0}, Y : {1}", ob.CurrentLocation.X, ob.CurrentLocation.Y), ChatType.System2);
                                    ReceiveChat(string.Format("文件 : {0}", npcOb.Info.FileName), ChatType.System2);
                                    break;
                            }
                        }
                        break;

                    case "CLEARQUESTS":
                        if (!IsGM && !Settings.TestServer) return;

                        player = parts.Length > 1 && IsGM ? Envir.GetPlayer(parts[1]) : this;

                        if (player == null)
                        {
                            ReceiveChat(parts[1] + " 未在线", ChatType.System);
                            return;
                        }

                        for (int i = player.CurrentQuests.Count - 1; i >= 0; i--)
                        {
                            SendUpdateQuest(player.CurrentQuests[i], QuestState.Remove);
                        }

                        player.CompletedQuests.Clear();
                        player.GetCompletedQuests();

                        break;

                    case "SETQUEST":
                        if ((!IsGM && !Settings.TestServer) || parts.Length < 3) return;

                        player = parts.Length > 3 && IsGM ? Envir.GetPlayer(parts[3]) : this;

                        if (player == null)
                        {
                            ReceiveChat(parts[3] + " 未在线", ChatType.System);
                            return;
                        }

                        int.TryParse(parts[1], out int questID);
                        int.TryParse(parts[2], out int questState);

                        if (questID < 1) return;

                        var activeQuest = player.CurrentQuests.FirstOrDefault(e => e.Index == questID);

                        //remove from active list
                        if (activeQuest != null)
                        {
                            player.SendUpdateQuest(activeQuest, QuestState.Remove);
                        }

                        switch (questState)
                        {
                            case 0: //cancel
                                if (player.CompletedQuests.Contains(questID))
                                {
                                    player.CompletedQuests.Remove(questID);
                                }
                                break;
                            case 1: //complete
                                if (!player.CompletedQuests.Contains(questID))
                                {
                                    player.CompletedQuests.Add(questID);
                                }
                                break;
                        }

                        player.GetCompletedQuests();
                        break;

                    case "TOGGLETRANSFORM":
                        if (HasBuff(BuffType.变形效果, out Buff transform))
                        {
                            if (transform.Paused)
                            {
                                UnpauseBuff(transform);
                            }
                            else
                            {
                                PauseBuff(transform);
                            }
                            RefreshStats();

                            hintstring = transform.Paused ? "禁用外形" : "启用外形";
                            ReceiveChat(hintstring, ChatType.Hint);
                        }                   
                        break;

                    case "STARTCONQUEST":
                        {
                            if ((!IsGM && !Settings.TestServer) || parts.Length < 2) return;
                            int conquestID;

                            if (parts.Length < 1)
                            {
                                ReceiveChat(string.Format("命令是 /StartConquest [ConquestID]"), ChatType.System);
                                return;
                            }

                            if (MyGuild == null)
                            {
                                ReceiveChat(string.Format("需要加入行会才能启动攻城战"), ChatType.System);
                                return;
                            }

                            else if (!int.TryParse(parts[1], out conquestID)) return;

                            ConquestObject tempConq = Envir.Conquests.FirstOrDefault(t => t.Info.Index == conquestID);

                            if (tempConq != null)
                            {
                                tempConq.StartType = ConquestType.强制启动;
                                tempConq.WarIsOn = !tempConq.WarIsOn;
                                tempConq.GuildInfo.AttackerID = MyGuild.Guildindex;
                            }
                            else return;
                            ReceiveChat(string.Format("{0} 攻城战开始", tempConq.Info.Name), ChatType.System);
                            MessageQueue.Enqueue(string.Format("{0} 攻城战开始", tempConq.Info.Name));

                            foreach (var pl in Envir.Players)
                            {
                                if (tempConq.WarIsOn)
                                {
                                    pl.ReceiveChat($"{tempConq.Info.Name} 战争开始", ChatType.System);
                                }
                                else
                                {
                                    pl.ReceiveChat($"{tempConq.Info.Name} 战争结束", ChatType.System);
                                }

                                pl.BroadcastInfo();
                            }
                        }
                        break;
                    case "RESETCONQUEST":
                        {
                            if ((!IsGM && !Settings.TestServer) || parts.Length < 2) return;
                            int conquestID;

                            if (parts.Length < 1)
                            {
                                ReceiveChat(string.Format("命令是 /ResetConquest [ConquestID]"), ChatType.System);
                                return;
                            }

                            if (MyGuild == null)
                            {
                                ReceiveChat(string.Format("需要加入行会才能启动攻城战"), ChatType.System);
                                return;
                            }

                            else if (!int.TryParse(parts[1], out conquestID)) return;

                            ConquestObject resetConq = Envir.Conquests.FirstOrDefault(t => t.Info.Index == conquestID);

                            if (resetConq != null && !resetConq.WarIsOn)
                            {
                                resetConq.Reset();
                                ReceiveChat(string.Format("{0} 已重置", resetConq.Info.Name), ChatType.System);
                            }
                            else
                            {
                                ReceiveChat("目前没有攻城战事", ChatType.System);
                            }
                        }
                        break;
                    case "GATES":
                        if (MyGuild == null || MyGuild.Conquest == null || !MyGuildRank.Options.HasFlag(GuildRankOptions.CanChangeRank) || MyGuild.Conquest.WarIsOn)
                        {
                            ReceiveChat(string.Format("没有权限控制城门"), ChatType.System);
                            return;
                        }

                        bool openClose = false;

                        if (parts.Length > 1)
                        {
                            string openclose = parts[1];

                            if (openclose.ToUpper() == "CLOSE")
                            {
                                openClose = true;
                            }
                            else if (openclose.ToUpper() == "OPEN")
                            {
                                openClose = false;
                            }
                            else
                            {
                                ReceiveChat(string.Format("必须输入 /打开城门 或 /关闭城门"), ChatType.System);
                                return;
                            }

                            for (int i = 0; i < MyGuild.Conquest.GateList.Count; i++)
                            {
                                if (MyGuild.Conquest.GateList[i].Gate != null && !MyGuild.Conquest.GateList[i].Gate.Dead)
                                {
                                    if (openClose)
                                    {
                                        MyGuild.Conquest.GateList[i].Gate.CloseDoor();
                                    }
                                    else
                                    {
                                        MyGuild.Conquest.GateList[i].Gate.OpenDoor();
                                    }
                                }
                            }
                        }
                        else
                        {
                            for (int i = 0; i < MyGuild.Conquest.GateList.Count; i++)
                            {
                                if (MyGuild.Conquest.GateList[i].Gate != null && !MyGuild.Conquest.GateList[i].Gate.Dead)
                                {
                                    if (!MyGuild.Conquest.GateList[i].Gate.Closed)
                                    {
                                        MyGuild.Conquest.GateList[i].Gate.CloseDoor();
                                        openClose = true;
                                    }
                                    else
                                    {
                                        MyGuild.Conquest.GateList[i].Gate.OpenDoor();
                                        openClose = false;
                                    }
                                }
                            }
                        }

                        if (openClose)
                        {
                            ReceiveChat(string.Format("城门 {0} 已关闭", MyGuild.Conquest.Info.Name), ChatType.System);
                        }
                        else
                        {
                            ReceiveChat(string.Format("城门 {0} 已打开", MyGuild.Conquest.Info.Name), ChatType.System);
                        }
                        break;

                    case "CHANGEFLAG":
                        if (MyGuild == null || MyGuild.Conquest == null || !MyGuildRank.Options.HasFlag(GuildRankOptions.CanChangeRank) || MyGuild.Conquest.WarIsOn)
                        {
                            ReceiveChat(string.Format("无权更改标志"), ChatType.System);
                            return;
                        }

                        ushort flag = (ushort)Envir.Random.Next(12);

                        if (parts.Length > 1)
                        {
                            ushort.TryParse(parts[1], out ushort temp);

                            if (temp <= 11) flag = temp;
                        }

                        MyGuild.Info.FlagImage = (ushort)(1000 + flag);

                        for (int i = 0; i < MyGuild.Conquest.FlagList.Count; i++)
                        {
                            MyGuild.Conquest.FlagList[i].UpdateImage();
                        }

                        break;
                    case "CHANGEFLAGCOLOUR":
                        {
                            if (MyGuild == null || MyGuild.Conquest == null || !MyGuildRank.Options.HasFlag(GuildRankOptions.CanChangeRank) || MyGuild.Conquest.WarIsOn)
                            {
                                ReceiveChat(string.Format("无权更改标志"), ChatType.System);
                                return;
                            }

                            byte r1 = (byte)Envir.Random.Next(255);
                            byte g1 = (byte)Envir.Random.Next(255);
                            byte b1 = (byte)Envir.Random.Next(255);

                            if (parts.Length > 3)
                            {
                                byte.TryParse(parts[1], out r1);
                                byte.TryParse(parts[2], out g1);
                                byte.TryParse(parts[3], out b1);
                            }

                            MyGuild.Info.FlagColour = Color.FromArgb(255, r1, g1, b1);

                            for (int i = 0; i < MyGuild.Conquest.FlagList.Count; i++)
                            {
                                MyGuild.Conquest.FlagList[i].UpdateColour();
                            }
                        }
                        break;
                    case "REVIVE":
                        if (!IsGM) return;

                        if (parts.Length < 2)
                        {
                            RefreshStats();
                            SetHP(Stats[Stat.HP]);
                            SetMP(Stats[Stat.MP]);
                            Revive(MaxHealth, true);
                        }
                        else
                        {
                            player = Envir.GetPlayer(parts[1]);
                            if (player == null) return;

                            player.Revive(MaxHealth, true);

                            Helpers.ChatSystem.SystemMessage(chatMessage: $"{player} 被管理员 {Name} 复活并恢复为满血状态");
                        }
                        break;
                    case "DELETESKILL":
                        if ((!IsGM) || parts.Length < 2) return;
                        Spell skill1;

                        if (!Enum.TryParse(parts.Length > 2 ? parts[2] : parts[1], true, out skill1)) return;

                        if (skill1 == Spell.None) return;

                        if (parts.Length > 2)
                        {
                            if (!IsGM) return;
                            player = Envir.GetPlayer(parts[1]);

                            if (player == null)
                            {
                                ReceiveChat(string.Format("找不到 {0} 这个玩家", parts[1]), ChatType.System);
                                return;
                            }
                        }
                        else
                        {
                            player = this;
                        }

                        if (player == null) return;

                        var magics = new UserMagic(skill1);
                        bool removed = false;

                        for (var i = player.Info.Magics.Count - 1; i >= 0; i--)
                        {
                            if (player.Info.Magics[i].Spell != skill1) continue;

                            player.Info.Magics.RemoveAt(i);
                            player.Enqueue(new S.RemoveMagic { PlaceId = i });
                            removed = true;
                        }

                        if (removed)
                        {
                            ReceiveChat(string.Format("{1} 的 {0} 技能已移除", skill1.ToString(), player.Name), ChatType.Hint);
                            player.ReceiveChat(string.Format("技能 {0} 已经移除", skill1), ChatType.Hint);

                            Helpers.ChatSystem.SystemMessage(chatMessage: $"{player} 的技能 {skill1.ToString()} 被管理员 {Name} 移除");
                        }
                        else
                        {
                            ReceiveChat(string.Format("未找到技能，无法删除操作"), ChatType.Hint);
                        }

                        break;
                    case "SETTIMER":
                        if (parts.Length < 4) return;

                        string key = parts[1];

                        if (!int.TryParse(parts[2], out int seconds)) return;
                        if (!byte.TryParse(parts[3], out byte timerType)) return;

                        SetTimer(key, seconds, timerType);

                        break;
                    case "SETLIGHT":
                        if ((!IsGM) || parts.Length < 2) return;

                        if (!byte.TryParse(parts[1], out byte light)) return;

                        Light = light;

                        Enqueue(GetUpdateInfo());
                        Broadcast(GetUpdateInfo());
                        break;
                    default:
                        break;
                }

                foreach (string command in Envir.CustomCommands)
                {
                    if (string.Compare(parts[0], command, true) != 0) continue;

                    CallDefaultNPC(DefaultNPCType.CustomCommand, parts[0]);
                }
            }
            else
            {
                message = String.Format("{0}:{1}", CurrentMap.Info.NoNames ? "?????" : Name, message);

                message = ProcessChatItems(message, null, linkedItems);

                p = new S.ObjectChat { ObjectID = ObjectID, Text = message, Type = ChatType.Normal };

                Enqueue(p);
                Broadcast(p);
            }
        }
        private string ProcessChatItems(string text, List<PlayerObject> recipients, List<ChatItem> chatItems)
        {
            if (chatItems == null)
            {
                return text;
            }

            foreach (var chatItem in chatItems)
            {
                Regex r = new Regex(chatItem.RegexInternalName, RegexOptions.IgnoreCase);

                text = r.Replace(text, chatItem.InternalName, 1);

                UserItem[] array;

                switch (chatItem.Grid)
                {
                    case MirGridType.Inventory:
                        array = Info.Inventory;
                        break;
                    case MirGridType.Storage:
                        array = Info.AccountInfo.Storage;
                        break;
                    case MirGridType.HeroInventory:
                        if (!HasHero || !HeroSpawned)
                            return text;
                        array = CurrentHero.Inventory;
                        break;
                    default:
                        continue;
                }

                UserItem item = null;

                for (int i = 0; i < array.Length; i++)
                {
                    item = array[i];
                    if (item == null || item.UniqueID != chatItem.UniqueID) continue;
                    break;
                }

                if (item != null)
                {
                    if (recipients == null)
                    {
                        for (int i = CurrentMap.Players.Count - 1; i >= 0; i--)
                        {
                            PlayerObject player = CurrentMap.Players[i];
                            if (player == this) continue;

                            if (player == null || player.Info == null || player.Node == null) continue;

                            if (Functions.InRange(CurrentLocation, player.CurrentLocation, Globals.DataRange))
                            {
                                player.CheckItem(item);

                                if (!player.Connection.SentChatItem.Contains(item))
                                {
                                    player.Enqueue(new S.NewChatItem { Item = item });
                                    player.Connection.SentChatItem.Add(item);
                                }
                            }
                        }
                    }
                    else
                    {
                        for (int i = 0; i < recipients.Count; i++)
                        {
                            PlayerObject player = recipients[i];
                            if (player == this) continue;

                            if (player == null || player.Info == null || player.Node == null) continue;

                            player.CheckItem(item);

                            if (!player.Connection.SentChatItem.Contains(item))
                            {
                                player.Enqueue(new S.NewChatItem { Item = item });
                                player.Connection.SentChatItem.Add(item);
                            }
                        }
                    }

                    if (!Connection.SentChatItem.Contains(item))
                    {
                        Enqueue(new S.NewChatItem { Item = item });
                        Connection.SentChatItem.Add(item);
                    }
                }
            }

            return text;
        }
        public void Turn(MirDirection dir)
        {
            _stepCounter = 0;

            if (CanMove)
            {
                ActionTime = Envir.Time + GetDelayTime(TurnDelay);

                Direction = dir;
                if (CheckMovement(CurrentLocation)) return;

                SafeZoneInfo szi = CurrentMap.GetSafeZone(CurrentLocation);

                if (szi != null)
                {
                    BindLocation = szi.Location;
                    BindMapIndex = CurrentMapIndex;
                    InSafeZone = true;
                }
                else
                    InSafeZone = false;

                Cell cell = CurrentMap.GetCell(CurrentLocation);

                for (int i = 0; i < cell.Objects.Count; i++)
                {
                    if (cell.Objects[i].Race != ObjectType.Spell) continue;
                    SpellObject ob = (SpellObject)cell.Objects[i];

                    ob.ProcessSpell(this);
                    //break;
                }

                if (TradePartner != null)
                    TradeCancel();

                if (ItemRentalPartner != null)
                    CancelItemRental();

                Broadcast(new S.ObjectTurn { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation });
            }

            Enqueue(new S.UserLocation { Direction = Direction, Location = CurrentLocation });
        }
        public void Harvest(MirDirection dir)
        {
            if (!CanMove)
            {
                Enqueue(new S.UserLocation { Direction = Direction, Location = CurrentLocation });
                return;
            }

            ActionTime = Envir.Time + HarvestDelay;

            Direction = dir;

            Enqueue(new S.UserLocation { Direction = Direction, Location = CurrentLocation });
            Broadcast(new S.ObjectHarvest { ObjectID = ObjectID, Direction = Direction, Location = CurrentLocation });

            Point front = Front;
            bool send = false;
            for (int d = 0; d <= 1; d++)
            {
                for (int y = front.Y - d; y <= front.Y + d; y++)
                {
                    if (y < 0) continue;
                    if (y >= CurrentMap.Height) break;

                    for (int x = front.X - d; x <= front.X + d; x += Math.Abs(y - front.Y) == d ? 1 : d * 2)
                    {
                        if (x < 0) continue;
                        if (x >= CurrentMap.Width) break;
                        if (!CurrentMap.ValidPoint(x, y)) continue;

                        Cell cell = CurrentMap.GetCell(x, y);
                        if (cell.Objects == null) continue;

                        for (int i = 0; i < cell.Objects.Count; i++)
                        {
                            MapObject ob = cell.Objects[i];
                            if (ob.Race != ObjectType.Monster || !ob.Dead || ob.Harvested) continue;

                            if (ob.EXPOwner != null && ob.EXPOwner != this && !IsMember(ob))
                            {
                                send = true;
                                continue;
                            }

                            if (ob.Harvest(this)) return;
                        }
                    }
                }
            }

            if (send)
                ReceiveChat("附近没有尸体", ChatType.System);
        }        
        private void CompleteQuest(IList<object> data)
        {
            QuestProgressInfo quest = (QuestProgressInfo)data[0];
            QuestAction questAction = (QuestAction)data[1];
            bool ignoreIfComplete = (bool)data[2];

            if (quest == null) return;

            switch (questAction)
            {
                case QuestAction.TimeExpired:
                    {
                        if (ignoreIfComplete && quest.Completed)
                        {
                            return;
                        }

                        AbandonQuest(quest.Info.Index);
                    }
                    break;
            }
        }
        private void CompleteNPC(IList<object> data)
        {
            uint npcid = (uint)data[0];
            int scriptid = (int)data[1];
            string page = (string)data[2];

            if (data.Count == 5)
            {
                Map map = (Map)data[3];
                Point coords = (Point)data[4];

                Teleport(map, coords);
            }

            NPCDelayed = true;

            if (page.Length > 0)
            {
                var script = NPCScript.Get(scriptid);
                script.Call(this, npcid, page.ToUpper());
            }
        }
        private UserItem GetBait(int count)
        {
            UserItem item = Info.Equipment[(int)EquipmentSlot.武器];
            if (item == null || item.Info.Type != ItemType.武器 || !item.Info.IsFishingRod) return null;

            UserItem bait = item.Slots[(int)FishingSlot.Bait];

            if (bait == null || bait.Count < count) return null;

            return bait;
        }
        private UserItem GetFishingItem(FishingSlot type)
        {
            UserItem item = Info.Equipment[(int)EquipmentSlot.武器];
            if (item == null || item.Info.Type != ItemType.武器 || !item.Info.IsFishingRod) return null;

            UserItem fishingItem = item.Slots[(int)type];

            if (fishingItem == null) return null;

            return fishingItem;
        }
        private void DeleteFishingItem(FishingSlot type)
        {
            UserItem item = Info.Equipment[(int)EquipmentSlot.武器];
            if (item == null || item.Info.Type != ItemType.武器 || !item.Info.IsFishingRod) return;

            UserItem slotItem = Info.Equipment[(int)EquipmentSlot.武器].Slots[(int)type];

            Enqueue(new S.DeleteItem { UniqueID = slotItem.UniqueID, Count = 1 });
            Info.Equipment[(int)EquipmentSlot.武器].Slots[(int)type] = null;

            Report.ItemChanged(slotItem, 1, 1);
        }
        private void DamagedFishingItem(FishingSlot type, int lossDura)
        {
            UserItem item = GetFishingItem(type);

            if (item != null)
            {
                if (item.CurrentDura <= 0)
                {

                    DeleteFishingItem(type);
                }
                else
                {
                    DamageItem(item, lossDura, true);
                }
            }
        }        
        public override bool CheckMovement(Point location)
        {
            if (Envir.Time < MovementTime) return false;

            //Script triggered coords
            for (int s = 0; s < CurrentMap.Info.ActiveCoords.Count; s++)
            {
                Point activeCoord = CurrentMap.Info.ActiveCoords[s];

                if (activeCoord != location) continue;

                CallDefaultNPC(DefaultNPCType.MapCoord, CurrentMap.Info.FileName, activeCoord.X, activeCoord.Y);
            }

            //Map movements
            for (int i = 0; i < CurrentMap.Info.Movements.Count; i++)
            {
                MovementInfo info = CurrentMap.Info.Movements[i];

                if (info.Source != location) continue;

                if (info.NeedHole)
                {
                    Cell cell = CurrentMap.GetCell(location);

                    if (cell.Objects == null ||
                        cell.Objects.Where(ob => ob.Race == ObjectType.Spell).All(ob => ((SpellObject)ob).Spell != Spell.DigOutZombie && ((SpellObject)ob).Spell != Spell.DigOutArmadillo))
                        continue;
                }

                if (info.ConquestIndex > 0)
                {
                    if (MyGuild == null || MyGuild.Conquest == null) continue;
                    if (MyGuild.Conquest.Info.Index != info.ConquestIndex) continue;
                }

                if (info.NeedMove) //use with ENTERMAP npc command
                {
                    NPCData["NPCMoveMap"] = Envir.GetMap(info.MapIndex);
                    NPCData["NPCMoveCoord"] = info.Destination;
                    continue;
                }

                Map temp = Envir.GetMap(info.MapIndex);

                if (temp == null || !temp.ValidPoint(info.Destination)) continue;

                CurrentMap.RemoveObject(this);
                Broadcast(new S.ObjectRemove { ObjectID = ObjectID });

                CompleteMapMovement(temp, info.Destination, CurrentMap, CurrentLocation);
                return true;
            }

            return false;
        }
        private void CompleteMapMovement(params object[] data)
        {
            if (this == null) return;
            Map temp = (Map)data[0];
            Point destination = (Point)data[1];
            Map checkmap = (Map)data[2];
            Point checklocation = (Point)data[3];

            if (CurrentMap != checkmap || CurrentLocation != checklocation) return;

            bool mapChanged = temp != CurrentMap;

            CurrentMap = temp;
            CurrentLocation = destination;

            CurrentMap.AddObject(this);

            MovementTime = Envir.Time + MovementDelay;

            Enqueue(new S.MapChanged
            {
                MapIndex = CurrentMap.Info.Index,
                FileName = CurrentMap.Info.FileName,
                Title = CurrentMap.Info.Title,
                Weather = CurrentMap.Info.WeatherParticles,
                MiniMap = CurrentMap.Info.MiniMap,
                BigMap = CurrentMap.Info.BigMap,
                Lights = CurrentMap.Info.Light,
                Location = CurrentLocation,
                Direction = Direction,
                MapDarkLight = CurrentMap.Info.MapDarkLight,
                Music = CurrentMap.Info.Music
            });

            if (RidingMount) RefreshMount();

            GetObjects();

            SafeZoneInfo szi = CurrentMap.GetSafeZone(CurrentLocation);

            if (szi != null)
            {
                BindLocation = szi.Location;
                BindMapIndex = CurrentMapIndex;
                InSafeZone = true;
            }
            else
                InSafeZone = false;

            if (mapChanged)
            {
                CallDefaultNPC(DefaultNPCType.MapEnter, CurrentMap.Info.FileName);
                GroupMemberMapNameChanged();
            }
            GetPlayerLocation();

            if (Info.Married != 0)
            {
                CharacterInfo Lover = Envir.GetCharacterInfo(Info.Married);
                PlayerObject player = Envir.GetPlayer(Lover.Name);

                if (player != null) player.GetRelationship(false);
            }

            CheckConquest(true);
        }
        public bool TeleportEscape(int attempts)
        {
            Map temp = Envir.GetMap(BindMapIndex);

            for (int i = 0; i < attempts; i++)
            {
                Point location = new Point(BindLocation.X + Envir.Random.Next(-100, 100),
                                           BindLocation.Y + Envir.Random.Next(-100, 100));

                if (Teleport(temp, location)) return true;
            }

            return false;
        }
        public override bool MagicTeleport(UserMagic magic)
        {
            Map temp = Envir.GetMap(BindMapIndex);
            int mapSizeX = temp.Width / (magic.Level + 1);
            int mapSizeY = temp.Height / (magic.Level + 1);

            for (int i = 0; i < 200; i++)
            {
                Point location = new Point(BindLocation.X + Envir.Random.Next(-mapSizeX, mapSizeX),
                                     BindLocation.Y + Envir.Random.Next(-mapSizeY, mapSizeY));

                if (Teleport(temp, location)) return true;
            }

            return false;
        }

        public override bool IsAttackTarget(HumanObject attacker)
        {            
            if (attacker == null || attacker.Node == null) return false;
            if (attacker.Race == ObjectType.Hero) attacker = ((HeroObject)attacker).Owner;
            if (Dead || InSafeZone || attacker.InSafeZone || attacker == this || GMGameMaster) return false;
            if (CurrentMap.Info.NoFight) return false;

            switch (attacker.AMode)
            {
                case AttackMode.All:
                    return true;
                case AttackMode.Group:
                    return GroupMembers == null || !GroupMembers.Contains(attacker);
                case AttackMode.Guild:
                    return MyGuild == null || MyGuild != attacker.MyGuild;
                case AttackMode.EnemyGuild:
                    return MyGuild != null && MyGuild.IsEnemy(attacker.MyGuild);
                case AttackMode.Peace:
                    return false;
                case AttackMode.RedBrown:
                    return PKPoints >= 200 || Envir.Time < BrownTime;
            }

            return true;
        }
        public override bool IsAttackTarget(MonsterObject attacker)
        {
            if (attacker == null || attacker.Node == null) return false;
            if (Dead || attacker.Master == this || GMGameMaster) return false;
            if (attacker.Info.AI == 980 || attacker.Info.AI == 981 || attacker.Info.AI == 982) return PKPoints >= 200;
            if (attacker.Master == null) return true;
            if (InSafeZone || attacker.InSafeZone || attacker.Master.InSafeZone) return false;

            if (LastHitter != attacker.Master && attacker.Master.LastHitter != this)
            {
                bool target = false;

                for (int i = 0; i < attacker.Master.Pets.Count; i++)
                {
                    if (attacker.Master.Pets[i].Target != this) continue;

                    target = true;
                    break;
                }

                if (!target)
                    return false;
            }

            switch (attacker.Master.AMode)
            {
                case AttackMode.All:
                    return true;
                case AttackMode.Group:
                    return GroupMembers == null || !GroupMembers.Contains(attacker.Master);
                case AttackMode.Guild:
                    return true;
                case AttackMode.EnemyGuild:
                    return false;
                case AttackMode.Peace:
                    return false;
                case AttackMode.RedBrown:
                    return PKPoints >= 200 || Envir.Time < BrownTime;
            }

            return true;
        }
        public override bool IsFriendlyTarget(HumanObject ally)
        {
            if (ally == this) return true;
            if (ally == Hero) return true;

            switch (ally.AMode)
            {
                case AttackMode.Group:
                    return GroupMembers != null && GroupMembers.Contains(ally);
                case AttackMode.RedBrown:
                    return PKPoints < 200 & Envir.Time > BrownTime;
                case AttackMode.Guild:
                    return MyGuild != null && MyGuild == ally.MyGuild;
                case AttackMode.EnemyGuild:
                    return MyGuild != null && !MyGuild.IsEnemy(ally.MyGuild);
                case AttackMode.All:
                    return false;
            }
            return true;
        }
        public override bool IsFriendlyTarget(MonsterObject ally)
        {
            if (ally.Race != ObjectType.Monster) return false;
            if (ally.Master == null) return false;

            switch (ally.Master.Race)
            {
                case ObjectType.Player:
                    if (!ally.Master.IsFriendlyTarget(this)) return false;
                    break;
                case ObjectType.Monster:
                    return false;
            }

            return true;
        }
        protected override void UpdateLooks(short OldLooks_Weapon)
        {
            base.UpdateLooks(OldLooks_Weapon);

            if (Globals.FishingRodShapes.Contains(OldLooks_Weapon) != Globals.FishingRodShapes.Contains(Looks_Weapon))
            {
                Enqueue(GetFishInfo());
            }
        }
        public override Packet GetInfo()
        {
            //should never use this but i leave it in for safety
            if (Observer) return null;

            string gName = "";
            string conquest = "";
            if (MyGuild != null)
            {
                gName = MyGuild.Name;
                if (MyGuild.Conquest != null)
                {
                    conquest = "[" + MyGuild.Conquest.Info.Name + "]";
                    gName = gName + conquest;
                }
                    
            }

            return new S.ObjectPlayer
            {
                ObjectID = ObjectID,
                Name = CurrentMap.Info.NoNames ? "?????" : Name,
                NameColour = NameColour,
                GuildName = CurrentMap.Info.NoNames ? "?????" : gName,
                GuildRankName = CurrentMap.Info.NoNames ? "?????" : MyGuildRank != null ? MyGuildRank.Name : "",
                Class = Class,
                Gender = Gender,
                Level = Level,
                Location = CurrentLocation,
                Direction = Direction,
                Hair = Hair,
                Weapon = Looks_Weapon,
				WeaponEffect = Looks_WeaponEffect,
				Armour = Looks_Armour,
                Light = Light,
                Poison = CurrentPoison,
                Dead = Dead,
                Hidden = Hidden,
                Effect = HasBuff(BuffType.魔法盾, out _) ? SpellEffect.MagicShieldUp : HasBuff(BuffType.金刚术, out _) ? SpellEffect.ElementalBarrierUp : SpellEffect.None,
                WingEffect = Looks_Wings,
                MountType = Mount.MountType,
                RidingMount = RidingMount,
                Fishing = Fishing,

                TransformType = TransformType,

                ElementOrbEffect = (uint)GetElementalOrbCount(),
                ElementOrbLvl = (uint)ElementsLevel,
                ElementOrbMax = (uint)Settings.OrbsExpList[Settings.OrbsExpList.Count - 1],

                Buffs = Buffs.Where(d => d.Info.Visible).Select(e => e.Type).ToList(),

                LevelEffects = LevelEffects
            };
        }
        public void EquipSlotItem(MirGridType grid, ulong id, int to, MirGridType gridTo, ulong idTo)
        {
            S.EquipSlotItem p = new S.EquipSlotItem { Grid = grid, UniqueID = id, To = to, GridTo = gridTo, Success = false };

            UserItem item = null;

            switch (gridTo)
            {
                case MirGridType.Mount:
                    item = Info.Equipment[(int)EquipmentSlot.坐骑];
                    break;
                case MirGridType.Fishing:
                    item = Info.Equipment[(int)EquipmentSlot.武器];
                    break;
                case MirGridType.Socket:
                    UserItem temp2;
                    for (int i = 0; i < Info.Equipment.Length; i++)
                    {
                        temp2 = Info.Equipment[i];
                        if (temp2 == null || temp2.UniqueID != idTo) continue;
                        item = temp2;
                        break;
                    }
                    for (int i = 0; i < Info.Inventory.Length; i++)
                    {
                        temp2 = Info.Inventory[i];
                        if (temp2 == null || temp2.UniqueID != idTo) continue;
                        item = temp2;
                        break;
                    }
                    break;
                default:
                    Enqueue(p);
                    return;
            }

            if (item == null || item.Slots == null)
            {
                Enqueue(p);
                return;
            }

            if (gridTo == MirGridType.Fishing && !item.Info.IsFishingRod)
            {
                Enqueue(p);
                return;
            }

            if (to < 0 || to >= item.Slots.Length)
            {
                Enqueue(p);
                return;
            }

            if (item.Slots[to] != null)
            {
                Enqueue(p);
                return;
            }

            UserItem[] array;
            switch (grid)
            {
                case MirGridType.Inventory:
                    array = Info.Inventory;
                    break;
                case MirGridType.Storage:
                    if (NPCPage == null || !String.Equals(NPCPage.Key, NPCScript.StorageKey, StringComparison.CurrentCultureIgnoreCase))
                    {
                        Enqueue(p);
                        return;
                    }
                    NPCObject ob = null;
                    for (int i = 0; i < CurrentMap.NPCs.Count; i++)
                    {
                        if (CurrentMap.NPCs[i].ObjectID != NPCObjectID) continue;
                        ob = CurrentMap.NPCs[i];
                        break;
                    }

                    if (ob == null || !Functions.InRange(ob.CurrentLocation, CurrentLocation, Globals.DataRange))
                    {
                        Enqueue(p);
                        return;
                    }

                    if (Info.Equipment[to] != null &&
                        Info.Equipment[to].Info.Bind.HasFlag(BindMode.DontStore))
                    {
                        Enqueue(p);
                        return;
                    }
                    array = Account.Storage;
                    break;
                default:
                    Enqueue(p);
                    return;
            }


            int index = -1;
            UserItem temp = null;

            for (int i = 0; i < array.Length; i++)
            {
                temp = array[i];
                if (temp == null || temp.UniqueID != id) continue;
                index = i;
                break;
            }

            if (temp == null || index == -1)
            {
                Enqueue(p);
                return;
            }

            if ((item.Info.IsFishingRod || item.Info.Type == ItemType.坐骑) && temp.Info.Type == ItemType.镶嵌宝石)
            {
                Enqueue(p);
                return;
            }

            if (gridTo == MirGridType.Socket && temp.Info.Type != ItemType.镶嵌宝石)
            {
                Enqueue(p);
                return;
            }

            if ((temp.SoulBoundId != -1) && (temp.SoulBoundId != Info.Index))
            {
                Enqueue(p);
                return;
            }

            if (CanUseItem(temp))
            {
                if (temp.Info.NeedIdentify && !temp.Identified)
                {
                    temp.Identified = true;
                    Enqueue(new S.RefreshItem { Item = temp });
                }

                switch (temp.Info.Shape)
                {
                    case 1:
                        if (item.Info.Type != ItemType.武器)
                        {
                            Enqueue(p);
                            return;
                        }
                        break;
                    case 2:
                        if (item.Info.Type != ItemType.盔甲)
                        {
                            Enqueue(p);
                            return;
                        }
                        break;
                    case 3:
                        if (item.Info.Type != ItemType.戒指 && item.Info.Type != ItemType.手镯 && item.Info.Type != ItemType.项链)
                        {
                            Enqueue(p);
                            return;
                        }
                        break;
                }

                //if ((temp.Info.BindOnEquip) && (temp.SoulBoundId == -1))
                //{
                //    temp.SoulBoundId = Info.Index;
                //    Enqueue(new S.RefreshItem { Item = temp });
                //}
                //if (UnlockCurse && Info.Equipment[to].Cursed)
                //    UnlockCurse = false;

                item.Slots[to] = temp;
                array[index] = null;

                p.Success = true;
                Enqueue(p);
                RefreshStats();

                Report.ItemMoved(temp, grid, gridTo, index, to);

                return;
            }

            Enqueue(p);
        }
        public void RemoveItem(MirGridType grid, ulong id, int to)
        {
            S.RemoveItem p = new S.RemoveItem { Grid = grid, UniqueID = id, To = to, Success = false };
            UserItem[] toArray, fromArray;
            MirGridType fromGrid;
            switch (grid)
            {
                case MirGridType.Inventory:
                    toArray = Info.Inventory;
                    fromArray = Info.Equipment;
                    fromGrid = MirGridType.Equipment;
                    break;
                case MirGridType.Storage:
                    if (NPCPage == null || !String.Equals(NPCPage.Key, NPCScript.StorageKey, StringComparison.CurrentCultureIgnoreCase))
                    {
                        Enqueue(p);
                        return;
                    }
                    NPCObject ob = null;
                    for (int i = 0; i < CurrentMap.NPCs.Count; i++)
                    {
                        if (CurrentMap.NPCs[i].ObjectID != NPCObjectID) continue;
                        ob = CurrentMap.NPCs[i];
                        break;
                    }

                    if (ob == null || !Functions.InRange(ob.CurrentLocation, CurrentLocation, Globals.DataRange))
                    {
                        Enqueue(p);
                        return;
                    }

                    if (!Account.IsValidStorageIndex(to))
                    {
                        Enqueue(p);
                        return;
                    }

                    toArray = Account.Storage;
                    fromArray = Info.Equipment;
                    fromGrid = MirGridType.Equipment;
                    break;
                case MirGridType.HeroInventory:
                    if (!HasHero || !HeroSpawned)
                    {
                        Enqueue(p);
                        return;
                    }
                    toArray = CurrentHero.Inventory;
                    fromArray = CurrentHero.Equipment;
                    fromGrid = MirGridType.HeroEquipment;
                    break;
                default:
                    Enqueue(p);
                    return;
            }

            if (to < 0 || to >= toArray.Length) return;

            UserItem temp = null;
            int index = -1;

            for (int i = 0; i < fromArray.Length; i++)
            {
                temp = fromArray[i];
                if (temp == null || temp.UniqueID != id) continue;
                index = i;
                break;
            }

            if (temp == null || index == -1)
            {
                Enqueue(p);
                return;
            }

            if (temp.Cursed && !UnlockCurse)
            {
                Enqueue(p);
                return;
            }

            if (temp.WeddingRing != -1)
            {
                Enqueue(p);
                return;
            }

            if (temp.Info.Bind.HasFlag(BindMode.DontStore) && grid == MirGridType.Storage)
            {
                Enqueue(p);
                return;
            }

            if (!CanRemoveItem(grid, temp)) return;

            if (temp.Cursed)
                UnlockCurse = false;

            if (toArray[to] == null)
            {
                fromArray[index] = null;

                toArray[to] = temp;
                p.Success = true;
                Enqueue(p);
                if (grid == MirGridType.HeroInventory)
                {
                    Hero.RefreshStats();
                    Hero.Broadcast(GetUpdateInfo());
                }
                else
                {
                    RefreshStats();
                    Broadcast(GetUpdateInfo());
                }

                Report.ItemMoved(temp, fromGrid, grid, index, to);

                return;
            }

            Enqueue(p);
        }
        public void RemoveSlotItem(MirGridType grid, ulong id, int to, MirGridType gridTo, ulong idFrom)
        {
            S.RemoveSlotItem p = new S.RemoveSlotItem { Grid = grid, UniqueID = id, To = to, GridTo = gridTo, Success = false };
            UserItem[] array;
            switch (gridTo)
            {
                case MirGridType.Inventory:
                    array = Info.Inventory;
                    break;
                case MirGridType.Storage:
                    if (NPCPage == null || !String.Equals(NPCPage.Key, NPCScript.StorageKey, StringComparison.CurrentCultureIgnoreCase))
                    {
                        Enqueue(p);
                        return;
                    }
                    NPCObject ob = null;
                    for (int i = 0; i < CurrentMap.NPCs.Count; i++)
                    {
                        if (CurrentMap.NPCs[i].ObjectID != NPCObjectID) continue;
                        ob = CurrentMap.NPCs[i];
                        break;
                    }

                    if (ob == null || !Functions.InRange(ob.CurrentLocation, CurrentLocation, Globals.DataRange))
                    {
                        Enqueue(p);
                        return;
                    }

                    if (!Account.IsValidStorageIndex(to))
                    {
                        Enqueue(p);
                        return;
                    }

                    array = Account.Storage;
                    break;
                default:
                    Enqueue(p);
                    return;
            }

            if (to < 0 || to >= array.Length) return;

            UserItem temp = null;
            UserItem slotTemp = null;
            int index = -1;

            switch (grid)
            {
                case MirGridType.Mount:
                    temp = Info.Equipment[(int)EquipmentSlot.坐骑];
                    break;
                case MirGridType.Fishing:
                    temp = Info.Equipment[(int)EquipmentSlot.武器];
                    break;
                case MirGridType.Socket:
                    UserItem temp2;
                    for (int i = 0; i < Info.Equipment.Length; i++)
                    {
                        temp2 = Info.Equipment[i];
                        if (temp2 == null || temp2.UniqueID != idFrom) continue;
                        temp = temp2;
                        break;
                    }
                    for (int i = 0; i < Info.Inventory.Length; i++)
                    {
                        temp2 = Info.Inventory[i];
                        if (temp2 == null || temp2.UniqueID != idFrom) continue;
                        temp = temp2;
                        break;
                    }
                    break;
                default:
                    Enqueue(p);
                    return;
            }

            if (temp == null || temp.Slots == null)
            {
                Enqueue(p);
                return;
            }

            if (grid == MirGridType.Fishing && !temp.Info.IsFishingRod)
            {
                Enqueue(p);
                return;
            }

            for (int i = 0; i < temp.Slots.Length; i++)
            {
                slotTemp = temp.Slots[i];
                if (slotTemp == null || slotTemp.UniqueID != id) continue;
                index = i;
                break;
            }

            if (slotTemp == null || index == -1)
            {
                Enqueue(p);
                return;
            }

            if (slotTemp.Cursed && !UnlockCurse)
            {
                Enqueue(p);
                return;
            }

            if (slotTemp.WeddingRing != -1)
            {
                Enqueue(p);
                return;
            }

            if (!CanRemoveItem(gridTo, slotTemp)) return;

            temp.Slots[index] = null;

            if (slotTemp.Cursed)
                UnlockCurse = false;

            if (array[to] == null)
            {
                array[to] = slotTemp;
                p.Success = true;
                Enqueue(p);
                RefreshStats();
                Broadcast(GetUpdateInfo());

                Report.ItemMoved(temp, grid, gridTo, index, to);

                return;
            }

            Enqueue(p);
        }
        public void MoveItem(MirGridType grid, int from, int to)
        {
            S.MoveItem p = new S.MoveItem { Grid = grid, From = from, To = to, Success = false };
            UserItem[] array;
            switch (grid)
            {
                case MirGridType.Inventory:
                    array = Info.Inventory;
                    break;
                case MirGridType.Storage:
                    if (NPCPage == null || !String.Equals(NPCPage.Key, NPCScript.StorageKey, StringComparison.CurrentCultureIgnoreCase))
                    {
                        Enqueue(p);
                        return;
                    }
                    NPCObject ob = null;
                    for (int i = 0; i < CurrentMap.NPCs.Count; i++)
                    {
                        if (CurrentMap.NPCs[i].ObjectID != NPCObjectID) continue;
                        ob = CurrentMap.NPCs[i];
                        break;
                    }

                    if (ob == null || !Functions.InRange(ob.CurrentLocation, CurrentLocation, Globals.DataRange))
                    {
                        Enqueue(p);
                        return;
                    }

                    if (!Account.IsValidStorageIndex(to) || !Account.IsValidStorageIndex(from))
                    {
                        Enqueue(p);
                        return;
                    }

                    array = Account.Storage;
                    break;
                case MirGridType.Trade:
                    array = Info.Trade;
                    TradeItem();
                    break;
                case MirGridType.Refine:
                    array = Info.Refine;
                    break;
                case MirGridType.HeroInventory:
                    if (!HasHero || !HeroSpawned)
                    {
                        Enqueue(p);
                        return;
                    }
                    array = CurrentHero.Inventory;
                    break;
                default:
                    Enqueue(p);
                    return;
            }

            if (from >= 0 && to >= 0 && from < array.Length && to < array.Length)
            {
                if (array[from] == null)
                {
                    Report.ItemError(grid, grid, from, to);
                    ReceiveChat("移动物品时发生错误 - 请报告移动的物品和时间", ChatType.System);
                    Enqueue(p);
                    return;
                }

                UserItem i = array[to];
                array[to] = array[from];

                Report.ItemMoved(array[to], grid, grid, from, to);

                array[from] = i;

                if (i != null)
                {
                    Report.ItemMoved(array[from], grid, grid, to, from);
                }

                p.Success = true;
                Enqueue(p);
                return;
            }

            Enqueue(p);
        }
        public void StoreItem(int from, int to)
        {
            S.StoreItem p = new S.StoreItem { From = from, To = to, Success = false };

            if (NPCPage == null || !String.Equals(NPCPage.Key, NPCScript.StorageKey, StringComparison.CurrentCultureIgnoreCase))
            {
                Enqueue(p);
                return;
            }
            NPCObject ob = null;
            for (int i = 0; i < CurrentMap.NPCs.Count; i++)
            {
                if (CurrentMap.NPCs[i].ObjectID != NPCObjectID) continue;
                ob = CurrentMap.NPCs[i];             
                break;
            }

            if (ob == null || !Functions.InRange(ob.CurrentLocation, CurrentLocation, Globals.DataRange))
            {
                Enqueue(p);
                return;
            }


            if (from < 0 || from >= Info.Inventory.Length)
            {
                Enqueue(p);
                return;
            }

            if (to < 0 || to >= Account.Storage.Length)
            {
                Enqueue(p);
                return;
            }

            if (!Account.IsValidStorageIndex(to))
            {
                Enqueue(p);
                return;
            }

            UserItem temp = Info.Inventory[from];

            if (temp == null)
            {
                Enqueue(p);
                return;
            }

            if (temp.Info.Bind.HasFlag(BindMode.DontStore))
            {
                Enqueue(p);
                return;
            }

            if (temp.RentalInformation != null && temp.RentalInformation.BindingFlags.HasFlag(BindMode.DontStore))
            {
                Enqueue(p);
                return;
            }

            if (Account.Storage[to] == null)
            {
                Account.Storage[to] = temp;
                Info.Inventory[from] = null;
                RefreshBagWeight();

                Report.ItemMoved(temp, MirGridType.Inventory, MirGridType.Storage, from, to);

                p.Success = true;
                Enqueue(p);
                return;
            }
            Enqueue(p);
        }
        public void TakeBackItem(int from, int to)
        {
            S.TakeBackItem p = new S.TakeBackItem { From = from, To = to, Success = false };

            if (NPCPage == null || !String.Equals(NPCPage.Key, NPCScript.StorageKey, StringComparison.CurrentCultureIgnoreCase))
            {
                Enqueue(p);
                return;
            }
            NPCObject ob = null;
            for (int i = 0; i < CurrentMap.NPCs.Count; i++)
            {
                if (CurrentMap.NPCs[i].ObjectID != NPCObjectID) continue;
                ob = CurrentMap.NPCs[i];
                break;
            }

            if (ob == null || !Functions.InRange(ob.CurrentLocation, CurrentLocation, Globals.DataRange))
            {
                Enqueue(p);
                return;
            }


            if (from < 0 || from >= Account.Storage.Length)
            {
                Enqueue(p);
                return;
            }

            if (!Account.IsValidStorageIndex(from))
            {
                Enqueue(p);
                return;
            }

            if (to < 0 || to >= Info.Inventory.Length)
            {
                Enqueue(p);
                return;
            }

            UserItem temp = Account.Storage[from];

            if (temp == null)
            {
                Enqueue(p);
                return;
            }

            if (Info.Inventory[to] == null)
            {
                Info.Inventory[to] = temp;
                Account.Storage[from] = null;

                Report.ItemMoved(temp, MirGridType.Storage, MirGridType.Inventory, from, to);

                p.Success = true;
                RefreshBagWeight();
                Enqueue(p);

                return;
            }
            Enqueue(p);
        }
        public void EquipItem(MirGridType grid, ulong id, int to)
        {
            S.EquipItem p = new S.EquipItem { Grid = grid, UniqueID = id, To = to, Success = false };

            if ((grid == MirGridType.Inventory || grid == MirGridType.Storage) && Fishing)
            {
                Enqueue(p);
                return;
            }

            UserItem[] toArray = null;
            MirGridType toGrid = MirGridType.Equipment;
            HumanObject actor = this;
            switch (grid)
            {
                case MirGridType.Inventory:
                case MirGridType.Storage:
                    toArray = Info.Equipment;
                    break;
                case MirGridType.HeroInventory:
                    if (HasHero && HeroSpawned && !Hero.Dead)
                    {
                        toArray = CurrentHero.Equipment;
                        toGrid = MirGridType.HeroEquipment;
                        actor = Hero;
                    }                        
                    break;
            }

            if (toArray == null || to < 0 || to >= toArray.Length)
            {
                Enqueue(p);
                return;
            }

            UserItem[] array;
            switch (grid)
            {
                case MirGridType.Inventory:
                    array = Info.Inventory;
                    break;
                case MirGridType.Storage:
                    if (NPCPage == null || !String.Equals(NPCPage.Key, NPCScript.StorageKey, StringComparison.CurrentCultureIgnoreCase))
                    {
                        Enqueue(p);
                        return;
                    }
                    NPCObject ob = null;
                    for (int i = 0; i < CurrentMap.NPCs.Count; i++)
                    {
                        if (CurrentMap.NPCs[i].ObjectID != NPCObjectID) continue;
                        ob = CurrentMap.NPCs[i];
                        break;
                    }

                    if (ob == null || !Functions.InRange(ob.CurrentLocation, CurrentLocation, Globals.DataRange))
                    {
                        Enqueue(p);
                        return;
                    }
                    array = Account.Storage;
                    break;
                case MirGridType.HeroInventory:
                    if (!HasHero || !HeroSpawned)
                    {
                        Enqueue(p);
                        return;
                    }
                    array = CurrentHero.Inventory;
                    break;
                default:
                    Enqueue(p);
                    return;
            }

            int index = -1;
            UserItem temp = null;

            for (int i = 0; i < array.Length; i++)
            {
                temp = array[i];
                if (temp == null || temp.UniqueID != id) continue;
                index = i;
                break;
            }

            if (temp == null || index == -1)
            {
                Enqueue(p);
                return;
            }

            if ((toArray[to] != null) && (toArray[to].Cursed) && (!UnlockCurse))
            {
                Enqueue(p);
                return;
            }

            if (grid == MirGridType.Storage)
            {
                if (!Account.IsValidStorageIndex(index))
                {
                    Enqueue(p);
                    return;
                }
            }

            if ((temp.SoulBoundId != -1) && (temp.SoulBoundId != Info.Index))
            {
                Enqueue(p);
                return;
            }

            if (toArray[to] != null)
                if (toArray[to].WeddingRing != -1)
                {
                    Enqueue(p);
                    return;
                }
            if (toArray[to] != null &&
                toArray[to].Info.Bind.HasFlag(BindMode.DontStore))
            {
                Enqueue(p);
                return;
            }

            if (actor.CanEquipItem(temp, to))
            {
                if (temp.Info.NeedIdentify && !temp.Identified)
                {
                    temp.Identified = true;
                    Enqueue(new S.RefreshItem { Item = temp });
                }
                if ((temp.Info.Bind.HasFlag(BindMode.BindOnEquip)) && (temp.SoulBoundId == -1))
                {
                    temp.SoulBoundId = Info.Index;
                    Enqueue(new S.RefreshItem { Item = temp });
                }

                if ((toArray[to] != null) && (toArray[to].Cursed) && (UnlockCurse))
                    UnlockCurse = false;

                array[index] = toArray[to];

                Report.ItemMoved(temp, toGrid, grid, to, index, "RemoveItem");

                toArray[to] = temp;

                Report.ItemMoved(temp, grid, toGrid, index, to);

                p.Success = true;
                Enqueue(p);
                if (toGrid == MirGridType.HeroEquipment)
                    Hero.RefreshStats();
                else
                    RefreshStats();

                //Broadcast(GetUpdateInfo());
                return;
            }
            Enqueue(p);
        }
        public void TakeBackHeroItem(int from, int to)
        {
            S.TakeBackHeroItem p = new S.TakeBackHeroItem { From = from, To = to, Success = false };

            if (!HasHero || !HeroSpawned || Hero.Dead)
            {
                Enqueue(p);
                return;
            }

            if (from < 0 || from >= CurrentHero.Inventory.Length)
            {
                Enqueue(p);
                return;
            }

            if (to < 0 || to >= Info.Inventory.Length)
            {
                Enqueue(p);
                return;
            }

            UserItem temp = CurrentHero.Inventory[from];

            if (temp == null)
            {
                Enqueue(p);
                return;
            }

            if (Info.Inventory[to] == null)
            {
                Info.Inventory[to] = temp;
                CurrentHero.Inventory[from] = null;

                Report.ItemMoved(temp, MirGridType.HeroInventory, MirGridType.Inventory, from, to);

                p.Success = true;
                RefreshBagWeight();
                Hero.RefreshBagWeight();
                Enqueue(p);

                return;
            }
            Enqueue(p);
        }
        public void TransferHeroItem(int from, int to)
        {
            S.TransferHeroItem p = new S.TransferHeroItem { From = from, To = to, Success = false };

            if (!HasHero || !HeroSpawned || Hero.Dead)
            {
                Enqueue(p);
                return;
            }

            if (from < 0 || from >= Info.Inventory.Length)
            {
                Enqueue(p);
                return;
            }

            if (to < 0 || to >= CurrentHero.Inventory.Length)
            {
                Enqueue(p);
                return;
            }

            UserItem temp = Info.Inventory[from];

            if (temp == null)
            {
                Enqueue(p);
                return;
            }

            if (temp.Info.Bind.HasFlag(BindMode.NoHero))
            {
                Enqueue(p);
                return;
            }

            if (temp.Weight + Hero.CurrentBagWeight > Hero.Stats[Stat.背包负重])
            {
                ReceiveChat("太重了，无法移动", ChatType.System);
                Enqueue(p);
                return;
            }

            if (CurrentHero.Inventory[to] == null)
            {
                CurrentHero.Inventory[to] = temp;
                Info.Inventory[from] = null;

                Report.ItemMoved(temp, MirGridType.Inventory, MirGridType.HeroInventory, from, to);

                p.Success = true;
                RefreshBagWeight();
                Hero.RefreshBagWeight();
                Enqueue(p);

                return;
            }
            Enqueue(p);
        }
        public override void UseItem(ulong id)
        {
            S.UseItem p = new S.UseItem { UniqueID = id, Grid = MirGridType.Inventory, Success = false };

            UserItem item = null;
            int index = -1;

            for (int i = 0; i < Info.Inventory.Length; i++)
            {
                item = Info.Inventory[i];
                if (item == null || item.UniqueID != id) continue;
                index = i;
                break;
            }

            if (item == null || index == -1 || !CanUseItem(item))
            {
                Enqueue(p);
                return;
            }

            if (Dead && !(item.Info.Type == ItemType.卷轴 && item.Info.Shape == 6))
            {
                Enqueue(p);
                return;
            }

            switch (item.Info.Type)
            {
                case ItemType.药水:
                    switch (item.Info.Shape)
                    {
                        case 0: //NormalPotion
                            PotHealthAmount = (ushort)Math.Min(ushort.MaxValue, PotHealthAmount + item.Info.Stats[Stat.HP]);
                            PotManaAmount = (ushort)Math.Min(ushort.MaxValue, PotManaAmount + item.Info.Stats[Stat.MP]);
                            break;
                        case 1: //SunPotion
                            ChangeHP(item.Info.Stats[Stat.HP]);
                            ChangeMP(item.Info.Stats[Stat.MP]);
                            break;
                        case 2: //MysteryWater
                            if (UnlockCurse)
                            {
                                ReceiveChat("解除了装备的诅咒", ChatType.Hint);
                                Enqueue(p);
                                return;
                            }
                            ReceiveChat("不能卸下被诅咒的装备", ChatType.Hint);
                            UnlockCurse = true;
                            break;
                        case 3: //Buff
                            {
                                int time = item.Info.Durability;

                                if (item.GetTotal(Stat.MaxDC) > 0)
                                    AddBuff(BuffType.攻击力提升, this, time * Settings.Minute, new Stats { [Stat.MaxDC] = item.GetTotal(Stat.MaxDC) });

                                if (item.GetTotal(Stat.MaxMC) > 0)
                                    AddBuff(BuffType.魔法力提升, this, time * Settings.Minute, new Stats { [Stat.MaxMC] = item.GetTotal(Stat.MaxMC) });

                                if (item.GetTotal(Stat.MaxSC) > 0)
                                    AddBuff(BuffType.道术力提升, this, time * Settings.Minute, new Stats { [Stat.MaxSC] = item.GetTotal(Stat.MaxSC) });

                                if (item.GetTotal(Stat.攻击速度) > 0)
                                    AddBuff(BuffType.攻击速度提升, this, time * Settings.Minute, new Stats { [Stat.攻击速度] = item.GetTotal(Stat.攻击速度) });

                                if (item.GetTotal(Stat.HP) > 0)
                                    AddBuff(BuffType.生命值提升, this, time * Settings.Minute, new Stats { [Stat.HP] = item.GetTotal(Stat.HP) });

                                if (item.GetTotal(Stat.MP) > 0)
                                    AddBuff(BuffType.法力值提升, this, time * Settings.Minute, new Stats { [Stat.MP] = item.GetTotal(Stat.MP) });

                                if (item.GetTotal(Stat.MaxAC) > 0)
                                    AddBuff(BuffType.防御提升, this, time * Settings.Minute, new Stats { [Stat.MaxAC] = item.GetTotal(Stat.MaxAC) });

                                if (item.GetTotal(Stat.MaxMAC) > 0)
                                    AddBuff(BuffType.魔法防御提升, this, time * Settings.Minute, new Stats { [Stat.MaxMAC] = item.GetTotal(Stat.MaxMAC) });

                                if (item.GetTotal(Stat.背包负重) > 0)
                                    AddBuff(BuffType.背包负重提升, this, time * Settings.Minute, new Stats { [Stat.背包负重] = item.GetTotal(Stat.背包负重) });

                                if (item.GetTotal(Stat.准确) > 0)
                                    AddBuff(BuffType.准确命中提升, this, time * Settings.Minute, new Stats { [Stat.准确] = item.GetTotal(Stat.准确) });

                                if (item.GetTotal(Stat.敏捷) > 0)
                                    AddBuff(BuffType.敏捷躲避提升, this, time * Settings.Minute, new Stats { [Stat.敏捷] = item.GetTotal(Stat.敏捷) });
                            }
                            break;
                        case 4: //Exp
                            {
                                int time = item.Info.Durability;
                                AddBuff(BuffType.获取经验提升, this, Settings.Minute * time, new Stats { [Stat.经验增长数率] = item.GetTotal(Stat.幸运) });
                            }
                            break;
                        case 5: //Drop
                            {
                                int time = item.Info.Durability;
                                AddBuff(BuffType.物品掉落提升, this, Settings.Minute * time, new Stats { [Stat.物品掉落数率] = item.GetTotal(Stat.幸运) });
                            }
                            break;
                        case 6:
                            PotHealthAmount = (ushort)Math.Min(ushort.MaxValue, PotHealthAmount + (Stats[Stat.HP] / 100) * (item.Info.Stats[Stat.生命值数率]));
                            PotManaAmount = (ushort)Math.Min(ushort.MaxValue, PotManaAmount + (Stats[Stat.MP] / 100) * (item.Info.Stats[Stat.法力值数率]));
                            break;
                        case 7:
                            ChangeHP((Stats[Stat.HP] / 100) * (item.Info.Stats[Stat.生命值数率]));
                            ChangeMP((Stats[Stat.MP] / 100) * (item.Info.Stats[Stat.法力值数率]));
                            break;
                        case 8:
                            {
                                int time = item.Info.Durability;
                                AddBuff(BuffType.技能经验提升, this, Settings.Minute * time, new Stats { [Stat.技能熟练度倍率] = 3 });
                            }
                            break;
                    }
                    break;
                case ItemType.卷轴:
                    UserItem temp;
                    switch (item.Info.Shape)
                    {
                        case 0: //DE
                            if (!TeleportEscape(20))
                            {
                                Enqueue(p);
                                return;
                            }
                            foreach (DelayedAction ac in ActionList.Where(u => u.Type == DelayedType.NPC))
                            {
                                ac.FlaggedToRemove = true;
                            }
                            break;
                        case 1: //TT
                            if (!Teleport(Envir.GetMap(BindMapIndex), BindLocation))
                            {
                                Enqueue(p);
                                return;
                            }
                            foreach (DelayedAction ac in ActionList.Where(u => u.Type == DelayedType.NPC))
                            {
                                ac.FlaggedToRemove = true;
                            }
                            break;
                        case 2: //RT
                            if (!TeleportRandom(200, item.Info.Durability))
                            {
                                Enqueue(p);
                                return;
                            }
                            foreach (DelayedAction ac in ActionList.Where(u => u.Type == DelayedType.NPC))
                            {
                                ac.FlaggedToRemove = true;
                            }
                            break;
                        case 3: //BenedictionOil
                            if (!TryLuckWeapon())
                            {
                                Enqueue(p);
                                return;
                            }
                            break;
                        case 4: //RepairOil
                            temp = Info.Equipment[(int)EquipmentSlot.武器];
                            if (temp == null || temp.MaxDura == temp.CurrentDura)
                            {
                                Enqueue(p);
                                return;
                            }
                            if (temp.Info.Bind.HasFlag(BindMode.DontRepair))
                            {
                                Enqueue(p);
                                return;
                            }
                            temp.MaxDura = (ushort)Math.Max(0, temp.MaxDura - Math.Min(5000, temp.MaxDura - temp.CurrentDura) / 30);

                            temp.CurrentDura = (ushort)Math.Min(temp.MaxDura, temp.CurrentDura + 5000);
                            temp.DuraChanged = false;

                            ReceiveChat("武器的部分被修复", ChatType.Hint);
                            Enqueue(new S.ItemRepaired { UniqueID = temp.UniqueID, MaxDura = temp.MaxDura, CurrentDura = temp.CurrentDura });
                            break;
                        case 5: //WarGodOil
                            temp = Info.Equipment[(int)EquipmentSlot.武器];
                            if (temp == null || temp.MaxDura == temp.CurrentDura)
                            {
                                Enqueue(p);
                                return;
                            }
                            if (temp.Info.Bind.HasFlag(BindMode.DontRepair) || (temp.Info.Bind.HasFlag(BindMode.NoSRepair)))
                            {
                                Enqueue(p);
                                return;
                            }
                            temp.CurrentDura = temp.MaxDura;
                            temp.DuraChanged = false;

                            ReceiveChat("武器已经完全修复", ChatType.Hint);
                            Enqueue(new S.ItemRepaired { UniqueID = temp.UniqueID, MaxDura = temp.MaxDura, CurrentDura = temp.CurrentDura });
                            break;
                        case 6: //ResurrectionScroll
                            if (CurrentMap.Info.NoReincarnation)
                            {
                                ReceiveChat(string.Format("非死亡状态禁用"), ChatType.System);
                                Enqueue(p);
                                return;
                            }
                            if (Dead)
                            {
                                MP = Stats[Stat.MP];
                                Revive(MaxHealth, true);
                            }
                            break;
                        case 7: //CreditScroll
                            if (item.Info.Price > 0)
                            {
                                GainCredit(item.Info.Price);
                                ReceiveChat(String.Format("{0} 信用资金已添加到帐户", item.Info.Price), ChatType.Hint);
                            }
                            break;
                        case 8: //MapShoutScroll
                            HasMapShout = true;
                            ReceiveChat("获得一次当前地图喊话", ChatType.Hint);
                            break;
                        case 9://ServerShoutScroll
                            HasServerShout = true;
                            ReceiveChat("获得一次全服务器喊话", ChatType.Hint);
                            break;
                        case 10://GuildSkillScroll
                            MyGuild.NewBuff(item.Info.Effect, false);
                            break;
                        case 11://HomeTeleport
                            if (MyGuild != null && MyGuild.Conquest != null && !MyGuild.Conquest.WarIsOn && MyGuild.Conquest.PalaceMap != null && !TeleportRandom(200, 0, MyGuild.Conquest.PalaceMap))
                            {
                                Enqueue(p);
                                return;
                            }
                            break;
                        case 12://LotteryTicket                                                                                    
                            if (Envir.Random.Next(item.Info.Effect * 32) == 1) // 1st prize : 1,000,000
                            {
                                ReceiveChat("一等奖！获得 1,000,000 金币", ChatType.Hint);
                                GainGold(1000000);
                            }
                            else if (Envir.Random.Next(item.Info.Effect * 16) == 1)  // 2nd prize : 200,000
                            {
                                ReceiveChat("二等奖! 获得 200,000 金币", ChatType.Hint);
                                GainGold(200000);
                            }
                            else if (Envir.Random.Next(item.Info.Effect * 8) == 1)  // 3rd prize : 100,000
                            {
                                ReceiveChat("三等奖! 获得 100,000 金币", ChatType.Hint);
                                GainGold(100000);
                            }
                            else if (Envir.Random.Next(item.Info.Effect * 4) == 1) // 4th prize : 10,000
                            {
                                ReceiveChat("四等奖! 获得 10,000 金币", ChatType.Hint);
                                GainGold(10000);
                            }
                            else if (Envir.Random.Next(item.Info.Effect * 2) == 1)  // 5th prize : 1,000
                            {
                                ReceiveChat("五等奖! 获得 1,000 金币", ChatType.Hint);
                                GainGold(1000);
                            }
                            else if (Envir.Random.Next(item.Info.Effect) == 1)  // 6th prize 500
                            {
                                ReceiveChat("六等奖! 获得 500 金币", ChatType.Hint);
                                GainGold(500);
                            }
                            else
                            {
                                ReceiveChat("没有中奖", ChatType.Hint);
                            }
                            break;
                        case 13://Hero unlock autopot
                            if (!HeroSpawned)
                            {
                                ReceiveChat(string.Format("没有激活的英雄无法使用该物品"), ChatType.System);
                                Enqueue(p);
                                return;
                            }
                            if (Hero.AutoPot)
                            {
                                ReceiveChat(string.Format("当前英雄已开启自动补给功能"), ChatType.System);
                                Enqueue(p);
                                return;
                            }
                            Hero.AutoPot = true;
                            Enqueue(new S.UnlockHeroAutoPot());
                            ReceiveChat("英雄背包自动补给功能已解锁", ChatType.Hint);
                            break;
                        case 14: //Increase maximum hero count
                            if (Info.MaximumHeroCount >= Settings.MaximumHeroCount)
                            {
                                ReceiveChat(string.Format("英雄持有数量已达上限"), ChatType.System);
                                Enqueue(p);
                                return;
                            }
                            Info.MaximumHeroCount++;
                            Array.Resize(ref Info.Heroes, Info.MaximumHeroCount);
                            break;
                        case 15: //Increase Hero Inventory
                            if (!HeroSpawned)
                            {
                                ReceiveChat(string.Format("没有激活的英雄无法使用该物品"), ChatType.System);
                                Enqueue(p);
                                return;
                            }
                            if (Hero.Info.Inventory.Length >= 42)
                            {
                                ReceiveChat(string.Format("当前英雄背包格已达到上限"), ChatType.System);
                                Enqueue(p);
                                return;
                            }
                            Hero.Enqueue(new S.ResizeInventory { Size = Hero.Info.ResizeInventory() });
                            ReceiveChat("当前英雄的背包格解锁成功", ChatType.Hint);
                            break;
                    }
                    break;
                case ItemType.技能书:
                    UserMagic magic = new UserMagic((Spell)item.Info.Shape);

                    if (magic.Info == null)
                    {
                        Enqueue(p);
                        return;
                    }

                    Info.Magics.Add(magic);
                    SendMagicInfo(magic);
                    RefreshStats();
                    break;
                case ItemType.特殊消耗品:
                    CallDefaultNPC(DefaultNPCType.UseItem, item.Info.Shape);
                    break;
                case ItemType.坐骑食物:
                    temp = Info.Equipment[(int)EquipmentSlot.坐骑];
                    if (temp == null || temp.MaxDura == temp.CurrentDura)
                    {
                        Enqueue(p);
                        return;
                    }

                    switch (item.Info.Shape)
                    {
                        case 0:
                            temp.MaxDura = (ushort)Math.Max(0, temp.MaxDura - Math.Min(1000, temp.MaxDura - (temp.CurrentDura / 30)));
                            break;
                        case 1:
                            break;
                    }

                    temp.CurrentDura = (ushort)Math.Min(temp.MaxDura, temp.CurrentDura + item.CurrentDura);
                    temp.DuraChanged = false;

                    ReceiveChat("坐骑已经吃饱", ChatType.Hint);
                    Enqueue(new S.ItemRepaired { UniqueID = temp.UniqueID, MaxDura = temp.MaxDura, CurrentDura = temp.CurrentDura });

                    RefreshStats();
                    break;
                case ItemType.灵物:
                    if (item.Info.Shape >= 20)
                    {
                        switch (item.Info.Shape)
                        {
                            case 20://Mirror
                                {
                                    Enqueue(new S.IntelligentCreatureEnableRename());
                                }
                                break;
                            case 21://BlackStone
                                {
                                    if (item.Count > 1) item.Count--;
                                    else Info.Inventory[index] = null;
                                    RefreshBagWeight();
                                    p.Success = true;
                                    Enqueue(p);
                                    BlackstoneRewardItem();
                                }
                                return;
                            case 22://Nuts
                                {
                                    if (CreatureSummoned)
                                    {
                                        for (int i = 0; i < Pets.Count; i++)
                                        {
                                            if (Pets[i].Race != ObjectType.Creature) continue;

                                            var pet = (IntelligentCreatureObject)Pets[i];
                                            if (pet.PetType != SummonedCreatureType) continue;
                                            pet.MaintainfoodTime = item.Info.Effect * Settings.Hour / 1000;
                                            break;
                                        }
                                    }
                                }
                                break;
                            case 23://FairyMoss, FreshwaterClam, Mackerel, Cherry
                                {
                                    if (CreatureSummoned)
                                    {
                                        for (int i = 0; i < Pets.Count; i++)
                                        {
                                            if (Pets[i].Race != ObjectType.Creature) continue;

                                            var pet = (IntelligentCreatureObject)Pets[i];
                                            if (pet.PetType != SummonedCreatureType) continue;
                                            if (pet.Fullness < 10000)
                                            {
                                                pet.IncreaseFullness(item.Info.Effect * 100);
                                            }
                                            break;
                                        }
                                    }
                                }
                                break;
                            case 24://WonderPill
                                {
                                    if (CreatureSummoned)
                                    {
                                        for (int i = 0; i < Pets.Count; i++)
                                        {
                                            if (Pets[i].Race != ObjectType.Creature) continue;

                                            var pet = (IntelligentCreatureObject)Pets[i];
                                            if (pet.PetType != SummonedCreatureType) continue;
                                            if (pet.Fullness == 0)
                                            {
                                                pet.IncreaseFullness(100);
                                            }
                                            break;
                                        }
                                    }
                                }
                                break;
                            case 25://Strongbox
                                {
                                    byte boxtype = item.Info.Effect;
                                    if (item.Count > 1) item.Count--;
                                    else Info.Inventory[index] = null;
                                    RefreshBagWeight();
                                    p.Success = true;
                                    Enqueue(p);
                                    StrongboxRewardItem(boxtype);
                                }
                                break;
                            case 26://Wonderdrug
                                {
                                    if (HasBuff(BuffType.奇异药水, out _))
                                    {
                                        ReceiveChat("特效已激活", ChatType.System);
                                        Enqueue(p);
                                        return;
                                    }

                                    var time = item.Info.Durability;

                                    AddBuff(BuffType.奇异药水, this, time * Settings.Minute, new Stats(item.AddedStats));
                                }
                                break;
                            case 27://FortuneCookies
                                break;
                            case 28://Knapsack
                                {
                                    var time = item.Info.Durability;

                                    AddBuff(BuffType.包容万斤, this, time * Settings.Minute, new Stats { [Stat.背包负重] = item.GetTotal(Stat.幸运) });
                                }
                                break;
                        }
                    }
                    else
                    {
                        int slotIndex = Info.IntelligentCreatures.Count;
                        UserIntelligentCreature petInfo = new UserIntelligentCreature((IntelligentCreatureType)item.Info.Shape, slotIndex, item.Info.Effect);
                        if (Info.CheckHasIntelligentCreature((IntelligentCreatureType)item.Info.Shape))
                        {
                            ReceiveChat("已拥有此灵物", ChatType.Hint);
                            petInfo = null;
                        }

                        if (petInfo == null || slotIndex >= 10)
                        {
                            Enqueue(p);
                            return;
                        }

                        ReceiveChat("获得新灵物 {" + petInfo.CustomName + "}.", ChatType.Hint);

                        Info.IntelligentCreatures.Add(petInfo);
                        Enqueue(petInfo.GetInfo());
                    }
                    break;
                case ItemType.外形物品: //Transforms
                    {
                        AddBuff(BuffType.变形效果, this, (Settings.Second * item.Info.Durability), new Stats(), values: item.Info.Shape);
                    }
                    break;
                case ItemType.装饰:

                    DecoObject decoOb = new DecoObject
                    {
                        Image = item.Info.Shape,
                        CurrentMap = CurrentMap,
                        CurrentLocation = CurrentLocation,
                    };

                    CurrentMap.AddObject(decoOb);
                    decoOb.Spawned();

                    Enqueue(decoOb.GetInfo());

                    break;
                case ItemType.怪物蛋:

                    var monsterID = item.Info.Stats[Stat.HP];
                    var spawnAsPet = item.Info.Shape == 1;
                    var conquestOnly = item.Info.Shape == 2;

                    var monsterInfo = Envir.GetMonsterInfo(monsterID);
                    if (monsterInfo == null) break;

                    MonsterObject monster = MonsterObject.GetMonster(monsterInfo);
                    if (monster == null) break;

                    if (spawnAsPet)
                    {
                        if (Pets.Count(t => !t.Dead && t.Race != ObjectType.Creature) >= Globals.MaxPets)
                        {
                            ReceiveChat("宠物数量已到上限", ChatType.Hint);
                            Enqueue(p);
                            return;
                        }

                        monster.Master = this;
                        monster.PetLevel = 0;
                        monster.MaxPetLevel = 7;

                        Pets.Add(monster);
                    }

                    if (conquestOnly)
                    {
                        var con = CurrentMap.GetConquest(CurrentLocation);
                        if (con == null)
                        {
                            ReceiveChat(string.Format("{0} 只能在攻城战期间召唤", monsterInfo.GameName), ChatType.Hint);
                            Enqueue(p);
                            return;
                        }
                    }

                    monster.Direction = Direction;
                    monster.ActionTime = Envir.Time + 5000;

                    if (!monster.Spawn(CurrentMap, Front))
                        monster.Spawn(CurrentMap, CurrentLocation);
                    break;
                case ItemType.攻城弹药:
                    //TODO;
                    break;
                case ItemType.封印:
                    HeroInfo heroInfo = Envir.GetHeroInfo(item.AddedStats[Stat.Hero]);
                    if (heroInfo == null || !AddHero(heroInfo))
                    {
                        Enqueue(p);
                        return;
                    }
                    break;
                default:
                    return;
            }

            if (item.Count > 1) item.Count--;
            else Info.Inventory[index] = null;
            RefreshBagWeight();

            Report.ItemChanged(item, 1, 1);

            p.Success = true;
            Enqueue(p);
        }
        public void HeroUseItem(ulong id)
        {
            if (!HasHero || !HeroSpawned)
                return;
            Hero.UseItem(id);
        }
        public void SplitItem(MirGridType grid, ulong id, ushort count)
        {
            S.SplitItem1 p = new S.SplitItem1 { Grid = grid, UniqueID = id, Count = count, Success = false };
            UserItem[] array;
            switch (grid)
            {
                case MirGridType.Inventory:
                    array = Info.Inventory;
                    break;
                case MirGridType.Storage:
                    if (NPCPage == null || !String.Equals(NPCPage.Key, NPCScript.StorageKey, StringComparison.CurrentCultureIgnoreCase))
                    {
                        Enqueue(p);
                        return;
                    }
                    NPCObject ob = null;
                    for (int i = 0; i < CurrentMap.NPCs.Count; i++)
                    {
                        if (CurrentMap.NPCs[i].ObjectID != NPCObjectID) continue;
                        ob = CurrentMap.NPCs[i];
                        break;
                    }

                    if (ob == null || !Functions.InRange(ob.CurrentLocation, CurrentLocation, Globals.DataRange))
                    {
                        Enqueue(p);
                        return;
                    }
                    array = Account.Storage;
                    break;
                default:
                    Enqueue(p);
                    return;
            }

            UserItem temp = null;


            var index = -1;
            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] == null || array[i].UniqueID != id) continue;
                index = i;
                temp = array[i];
                break;
            }

            if (temp == null || index == -1 || count >= temp.Count || FreeSpace(array) == 0 || count < 1)
            {
                Enqueue(p);
                return;
            }

            if (grid == MirGridType.Storage)
            {
                var nindex = -1;
                for (int i = 0; i < array.Length; i++)
                {
                    if (array[i] != null) continue;
                    nindex = i;
                    break;
                }

                if (!Account.IsValidStorageIndex(index) || !Account.IsValidStorageIndex(nindex))
                {
                    Enqueue(p);
                    return;
                }
            }

            temp.Count -= count;

            var originalItem = temp;

            temp = Envir.CreateFreshItem(temp.Info);
            temp.Count = count;

            Report.ItemSplit(originalItem, temp, grid);

            p.Success = true;
            Enqueue(p);
            Enqueue(new S.SplitItem { Item = temp, Grid = grid });

            if (grid == MirGridType.Inventory && (temp.Info.Type == ItemType.药水 || temp.Info.Type == ItemType.卷轴 || temp.Info.Type == ItemType.护身符 || (temp.Info.Type == ItemType.特殊消耗品 && temp.Info.Effect == 1)))
            {
                if (temp.Info.Type == ItemType.药水 || temp.Info.Type == ItemType.卷轴 || (temp.Info.Type == ItemType.特殊消耗品 && temp.Info.Effect == 1))
                {
                    for (int i = PotionBeltMinimum; i < PotionBeltMaximum; i++)
                    {
                        if (array[i] != null) continue;
                        array[i] = temp;
                        RefreshBagWeight();
                        return;
                    }
                }
                else if (temp.Info.Type == ItemType.护身符)
                {
                    for (int i = AmuletBeltMinimum; i < AmuletBeltMaximum; i++)
                    {
                        if (array[i] != null) continue;
                        array[i] = temp;
                        RefreshBagWeight();
                        return;
                    }
                }
            }

            for (int i = BeltSize; i < array.Length; i++)
            {
                if (array[i] != null) continue;
                array[i] = temp;
                RefreshBagWeight();
                return;
            }

            for (int i = 0; i < BeltSize; i++)
            {
                if (array[i] != null) continue;
                array[i] = temp;
                RefreshBagWeight();
                return;
            }
        }
        public void MergeItem(MirGridType gridFrom, MirGridType gridTo, ulong fromID, ulong toID)
        {
            S.MergeItem p = new S.MergeItem { GridFrom = gridFrom, GridTo = gridTo, IDFrom = fromID, IDTo = toID, Success = false };

            UserItem[] arrayFrom;

            switch (gridFrom)
            {
                case MirGridType.Inventory:
                    arrayFrom = Info.Inventory;
                    break;
                case MirGridType.Storage:
                    if (NPCPage == null || !String.Equals(NPCPage.Key, NPCScript.StorageKey, StringComparison.CurrentCultureIgnoreCase))
                    {
                        Enqueue(p);
                        return;
                    }
                    NPCObject ob = null;
                    for (int i = 0; i < CurrentMap.NPCs.Count; i++)
                    {
                        if (CurrentMap.NPCs[i].ObjectID != NPCObjectID) continue;
                        ob = CurrentMap.NPCs[i];
                        break;
                    }

                    if (ob == null || !Functions.InRange(ob.CurrentLocation, CurrentLocation, Globals.DataRange))
                    {
                        Enqueue(p);
                        return;
                    }
                    arrayFrom = Account.Storage;
                    break;
                case MirGridType.Equipment:
                    arrayFrom = Info.Equipment;
                    break;
                case MirGridType.Fishing:
                    if (Info.Equipment[(int)EquipmentSlot.武器] == null || !Info.Equipment[(int)EquipmentSlot.武器].Info.IsFishingRod)
                    {
                        Enqueue(p);
                        return;
                    }
                    arrayFrom = Info.Equipment[(int)EquipmentSlot.武器].Slots;
                    break;
                case MirGridType.HeroInventory:
                    if (!HasHero || !HeroSpawned)
                    {
                        Enqueue(p);
                        return;
                    }
                    arrayFrom = CurrentHero.Inventory;
                    break;
                case MirGridType.HeroEquipment:
                    if (!HasHero || !HeroSpawned)
                    {
                        Enqueue(p);
                        return;
                    }
                    arrayFrom = CurrentHero.Equipment;
                    break;
                default:
                    Enqueue(p);
                    return;
            }

            UserItem[] arrayTo;
            switch (gridTo)
            {
                case MirGridType.Inventory:
                    arrayTo = Info.Inventory;
                    break;
                case MirGridType.Storage:
                    if (NPCPage == null || !String.Equals(NPCPage.Key, NPCScript.StorageKey, StringComparison.CurrentCultureIgnoreCase))
                    {
                        Enqueue(p);
                        return;
                    }
                    NPCObject ob = null;
                    for (int i = 0; i < CurrentMap.NPCs.Count; i++)
                    {
                        if (CurrentMap.NPCs[i].ObjectID != NPCObjectID) continue;
                        ob = CurrentMap.NPCs[i];
                        break;
                    }

                    if (ob == null || !Functions.InRange(ob.CurrentLocation, CurrentLocation, Globals.DataRange))
                    {
                        Enqueue(p);
                        return;
                    }
                    arrayTo = Account.Storage;
                    break;
                case MirGridType.Equipment:
                    arrayTo = Info.Equipment;
                    break;
                case MirGridType.Fishing:
                    if (Info.Equipment[(int)EquipmentSlot.武器] == null || !Info.Equipment[(int)EquipmentSlot.武器].Info.IsFishingRod)
                    {
                        Enqueue(p);
                        return;
                    }
                    arrayTo = Info.Equipment[(int)EquipmentSlot.武器].Slots;
                    break;
                case MirGridType.HeroInventory:
                    if (!HasHero || !HeroSpawned)
                    {
                        Enqueue(p);
                        return;
                    }
                    arrayTo = CurrentHero.Inventory;
                    break;
                case MirGridType.HeroEquipment:
                    if (!HasHero || !HeroSpawned)
                    {
                        Enqueue(p);
                        return;
                    }
                    arrayTo = CurrentHero.Equipment;
                    break;
                default:
                    Enqueue(p);
                    return;
            }

            UserItem tempFrom = null;
            int index = -1;

            for (int i = 0; i < arrayFrom.Length; i++)
            {
                if (arrayFrom[i] == null || arrayFrom[i].UniqueID != fromID) continue;
                index = i;
                tempFrom = arrayFrom[i];
                break;
            }

            if (tempFrom == null || tempFrom.Info.StackSize == 1 || index == -1)
            {
                Enqueue(p);
                return;
            }

            if (gridFrom == MirGridType.Storage)
            {
                if (!Account.IsValidStorageIndex(index))
                {
                    Enqueue(p);
                    return;
                }
            }


            UserItem tempTo = null;
            int toIndex = -1;

            for (int i = 0; i < arrayTo.Length; i++)
            {
                if (arrayTo[i] == null || arrayTo[i].UniqueID != toID) continue;
                toIndex = i;
                tempTo = arrayTo[i];
                break;
            }

            if (tempTo == null || tempTo.Info != tempFrom.Info || tempTo.Count == tempTo.Info.StackSize)
            {
                Enqueue(p);
                return;
            }

            if (gridTo == MirGridType.Storage)
            {
                if (!Account.IsValidStorageIndex(toIndex))
                {
                    Enqueue(p);
                    return;
                }
            }

            if (tempTo.Info.Type != ItemType.护身符 && (gridFrom == MirGridType.Equipment || gridTo == MirGridType.Equipment))
            {
                Enqueue(p);
                return;
            }

            if (tempTo.Info.Type != ItemType.鱼饵 && (gridFrom == MirGridType.Fishing || gridTo == MirGridType.Fishing))
            {
                Enqueue(p);
                return;
            }

            if (tempFrom.Count <= tempTo.Info.StackSize - tempTo.Count)
            {
                tempTo.Count += tempFrom.Count;
                arrayFrom[index] = null;
            }
            else
            {
                tempFrom.Count -= (ushort)(tempTo.Info.StackSize - tempTo.Count);
                tempTo.Count = tempTo.Info.StackSize;
            }

            Report.ItemMerged(tempFrom, tempTo, index, toIndex, gridFrom, gridTo);

            TradeUnlock();

            p.Success = true;
            Enqueue(p);
            RefreshStats();
        }
        public void CombineItem(MirGridType grid, ulong fromID, ulong toID)
        {
            S.CombineItem p = new S.CombineItem { Grid = grid, IDFrom = fromID, IDTo = toID, Success = false };

            UserItem[] array = null;
            switch (grid)
            {
                case MirGridType.Inventory:
                    array = Info.Inventory;
                    break;
                case MirGridType.HeroInventory:
                    if (HasHero && HeroSpawned)
                        array = CurrentHero.Inventory;
                    break;
            }

            if (array == null)
            {
                Enqueue(p);
                return;
            }

            UserItem tempFrom = null;
            UserItem tempTo = null;
            int indexFrom = -1;
            int indexTo = -1;

            if (Dead)
            {
                Enqueue(p);
                return;
            }

            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] == null || array[i].UniqueID != fromID) continue;
                indexFrom = i;
                tempFrom = array[i];
                break;
            }

            if (tempFrom == null || indexFrom == -1)
            {
                Enqueue(p);
                return;
            }

            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] == null || array[i].UniqueID != toID) continue;
                indexTo = i;
                tempTo = array[i];
                break;
            }

            if (tempTo == null || indexTo == -1)
            {
                Enqueue(p);
                return;
            }

            if ((byte)tempTo.Info.Type < 1 || (byte)tempTo.Info.Type > 11)
            {
                Enqueue(p);
                return;
            }

            bool canRepair = false, canUpgrade = false, canSlotUpgrade = false, canSeal = false;

            if (tempFrom.Info.Type != ItemType.宝玉神珠)
            {
                Enqueue(p);
                return;
            }

            switch (tempFrom.Info.Shape)
            {
                case 1: //BoneHammer
                case 2: //SewingSupplies
                case 5: //SpecialHammer
                case 6: //SpecialSewingSupplies

                    if (tempTo.Info.Bind.HasFlag(BindMode.DontRepair))
                    {
                        Enqueue(p);
                        return;
                    }

                    switch (tempTo.Info.Type)
                    {
                        case ItemType.武器:
                        case ItemType.项链:
                        case ItemType.戒指:
                        case ItemType.手镯:
                            if (tempFrom.Info.Shape == 1 || tempFrom.Info.Shape == 5)
                                canRepair = true;
                            break;
                        case ItemType.盔甲:
                        case ItemType.头盔:
                        case ItemType.靴子:
                        case ItemType.腰带:
                            if (tempFrom.Info.Shape == 2 || tempFrom.Info.Shape == 6)
                                canRepair = true;
                            break;
                        default:
                            canRepair = false;
                            break;
                    }

                    if (canRepair != true)
                    {
                        Enqueue(p);
                        return;
                    }

                    if (tempTo.CurrentDura == tempTo.MaxDura)
                    {
                        ReceiveChat("物品不需要修理", ChatType.Hint);
                        Enqueue(p);
                        return;
                    }
                    break;
                case 7: //slots
                    if (tempTo.Info.Bind.HasFlag(BindMode.DontUpgrade) || tempTo.Info.Unique != SpecialItemMode.None)
                    {
                        ReceiveChat("无法对禁止升级类物品进行嵌孔操作", ChatType.Hint);
                        Enqueue(p);
                        return;
                    }
                    if (tempTo.RentalInformation != null && tempTo.RentalInformation.BindingFlags.HasFlag(BindMode.DontUpgrade))
                    {
                        ReceiveChat("无法对租赁类物品进行嵌孔操作", ChatType.Hint);
                        Enqueue(p);
                        return;
                    }
                    if (!ValidGemForItem(tempFrom, (byte)tempTo.Info.Type))
                    {
                        ReceiveChat("无效操作", ChatType.Hint);
                        Enqueue(p);
                        return;
                    }
                    if (tempTo.Info.RandomStats == null)
                    {
                        ReceiveChat("非嵌孔类物品", ChatType.Hint);
                        Enqueue(p);
                        return;
                    }
                    if (tempTo.Info.RandomStats.SlotMaxStat == 0)
                    {
                        ReceiveChat("该物品不能嵌孔", ChatType.Hint);
                        Enqueue(p);
                        return;
                    }
                    if (tempTo.Info.RandomStats.SlotMaxStat <= tempTo.Slots.Length)
                    {
                        ReceiveChat("物品孔位已达上限", ChatType.Hint);
                        Enqueue(p);
                        return;
                    }

                    canSlotUpgrade = true;
                    break;
                case 8: //Seal
                    if (tempTo.Info.Bind.HasFlag(BindMode.DontUpgrade) || tempTo.Info.Unique != SpecialItemMode.None)
                    {
                        ReceiveChat("无法对禁止升级类物品进行锁定操作", ChatType.Hint);
                        Enqueue(p);
                        return;
                    }
                    if (tempTo.SealedInfo != null && tempTo.SealedInfo.ExpiryDate > Envir.Now)
                    {
                        ReceiveChat("该物品已经上锁", ChatType.Hint);
                        Enqueue(p);
                        return;
                    }
                    if (tempTo.SealedInfo != null && tempTo.SealedInfo.NextSealDate > Envir.Now)
                    {
                        double remainingSeconds = (tempTo.SealedInfo.NextSealDate - Envir.Now).TotalSeconds;

                        ReceiveChat($"该物品需要{Functions.PrintTimeSpanFromSeconds(remainingSeconds, false)}后才能再次上锁", ChatType.Hint);
                        Enqueue(p);
                        return;
                    }

                    canSeal = true;
                    break;
                case 3: //gems
                case 4: //orbs
                    if (tempTo.Info.Bind.HasFlag(BindMode.DontUpgrade) || tempTo.Info.Unique != SpecialItemMode.None)
                    {
                        Enqueue(p);
                        return;
                    }

                    if (tempTo.RentalInformation != null && tempTo.RentalInformation.BindingFlags.HasFlag(BindMode.DontUpgrade))
                    {
                        Enqueue(p);
                        return;
                    }

                    if ((tempTo.GemCount >= tempFrom.Info.Stats[Stat.暴击伤害]) || (GetCurrentStatCount(tempFrom, tempTo) >= tempFrom.Info.Stats[Stat.吸血数率]))
                    {
                        ReceiveChat("赋能已达上限", ChatType.Hint);
                        Enqueue(p);
                        return;
                    }

                    int successchance = tempFrom.Info.Stats[Stat.反弹伤害];

                    // Gem is only affected by the stat applied.
                    // Drop rate per gem won't work if gems add more than 1 stat, i.e. DC + 2 per gem.
                    if (Settings.GemStatIndependent)
                    {
                        Stat GemType = GetGemType(tempFrom);

                        switch (GemType)
                        {
                            case Stat.MaxAC:
                                successchance *= (int)tempTo.AddedStats[Stat.MaxAC];
                                break;

                            case Stat.MaxMAC:
                                successchance *= (int)tempTo.AddedStats[Stat.MaxMAC];
                                break;

                            case Stat.MaxDC:
                                successchance *= (int)tempTo.AddedStats[Stat.MaxDC];
                                break;

                            case Stat.MaxMC:
                                successchance *= (int)tempTo.AddedStats[Stat.MaxMC];
                                break;

                            case Stat.MaxSC:
                                successchance *= (int)tempTo.AddedStats[Stat.MaxSC];
                                break;

                            case Stat.攻击速度:
                                successchance *= (int)tempTo.AddedStats[Stat.攻击速度];
                                break;

                            case Stat.准确:
                                successchance *= (int)tempTo.AddedStats[Stat.准确];
                                break;

                            case Stat.敏捷:
                                successchance *= (int)tempTo.AddedStats[Stat.敏捷];
                                break;

                            case Stat.冰冻伤害:
                                successchance *= (int)tempTo.AddedStats[Stat.冰冻伤害];
                                break;

                            case Stat.毒素伤害:
                                successchance *= (int)tempTo.AddedStats[Stat.毒素伤害];
                                break;

                            case Stat.魔法躲避:
                                successchance *= (int)tempTo.AddedStats[Stat.魔法躲避];
                                break;

                            case Stat.毒物躲避:
                                successchance *= (int)tempTo.AddedStats[Stat.毒物躲避];
                                break;

                            // These attributes may not work as more than 1 stat is
                            // added per gem, i.e + 40 HP.

                            case Stat.HP:
                                successchance *= (int)tempTo.AddedStats[Stat.HP];
                                break;

                            case Stat.MP:
                                successchance *= (int)tempTo.AddedStats[Stat.MP];
                                break;

                            case Stat.生命恢复:
                                successchance *= (int)tempTo.AddedStats[Stat.生命恢复];
                                break;
                                
                            // I don't know if this conflicts with benes.
                            case Stat.幸运:
                                successchance *= (int)tempTo.AddedStats[Stat.幸运];
                                break;

                            case Stat.强度:
                                successchance *= (int)tempTo.AddedStats[Stat.强度];
                                break;

                            case Stat.中毒恢复:
                                successchance *= (int)tempTo.AddedStats[Stat.中毒恢复];
                                break;


                            /*
                                 Currently not supported.
                                 Missing item definitions.

                                 case StatType.HP_Precent:
                                 case StatType.MP_Precent:
                                 case StatType.MP_Regen:
                                 case StatType.Holy:
                                 case StatType.Durability:


                            */
                            default:
                                successchance *= (int)tempTo.GemCount;
                                break;

                        }
                    }
                    // Gem is affected by the total added stats on the item.
                    else
                    {
                        successchance *= (int)tempTo.GemCount;
                    }

                    successchance = successchance >= tempFrom.Info.Stats[Stat.暴击倍率] ? 0 : (tempFrom.Info.Stats[Stat.暴击倍率] - successchance) + Stats[Stat.宝石成功数率];

                    //check if combine will succeed
                    bool succeeded = Envir.Random.Next(100) < successchance;
                    canUpgrade = true;

                    byte itemType = (byte)tempTo.Info.Type;

                    if (!ValidGemForItem(tempFrom, itemType))
                    {
                        ReceiveChat("赋能无效", ChatType.Hint);
                        Enqueue(p);
                        return;
                    }

                    if (tempFrom.GetTotal(Stat.MaxDC) > 0)
                    {
                        if (succeeded) tempTo.AddedStats[Stat.MaxDC] += tempFrom.GetTotal(Stat.MaxDC);
                    }

                    else if (tempFrom.GetTotal(Stat.MaxMC) > 0)
                    {
                        if (succeeded) tempTo.AddedStats[Stat.MaxMC] += tempFrom.GetTotal(Stat.MaxMC);
                    }

                    else if (tempFrom.GetTotal(Stat.MaxSC) > 0)
                    {
                        if (succeeded) tempTo.AddedStats[Stat.MaxSC] += tempFrom.GetTotal(Stat.MaxSC);
                    }

                    else if (tempFrom.GetTotal(Stat.MaxAC) > 0)
                    {
                        if (succeeded) tempTo.AddedStats[Stat.MaxAC] += tempFrom.GetTotal(Stat.MaxAC);
                    }

                    else if (tempFrom.GetTotal(Stat.MaxMAC) > 0)
                    {
                        if (succeeded) tempTo.AddedStats[Stat.MaxMAC] += tempFrom.GetTotal(Stat.MaxMAC);
                    }

                    else if ((tempFrom.Info.Durability) > 0)
                    {
                        if (succeeded) tempTo.MaxDura = (ushort)Math.Min(ushort.MaxValue, tempTo.MaxDura + tempFrom.MaxDura);
                    }

                    else if (tempFrom.GetTotal(Stat.攻击速度) > 0)
                    {
                        if (succeeded) tempTo.AddedStats[Stat.攻击速度] += tempFrom.GetTotal(Stat.攻击速度);
                    }

                    else if (tempFrom.GetTotal(Stat.敏捷) > 0)
                    {
                        if (succeeded) tempTo.AddedStats[Stat.敏捷] += tempFrom.GetTotal(Stat.敏捷);
                    }

                    else if (tempFrom.GetTotal(Stat.准确) > 0)
                    {
                        if (succeeded) tempTo.AddedStats[Stat.准确] += tempFrom.GetTotal(Stat.准确);
                    }

                    else if (tempFrom.GetTotal(Stat.毒素伤害) > 0)
                    {
                        if (succeeded) tempTo.AddedStats[Stat.毒素伤害] += tempFrom.GetTotal(Stat.毒素伤害);
                    }

                    else if (tempFrom.GetTotal(Stat.冰冻伤害) > 0)
                    {
                        if (succeeded) tempTo.AddedStats[Stat.冰冻伤害] += tempFrom.GetTotal(Stat.冰冻伤害);
                    }

                    else if (tempFrom.GetTotal(Stat.魔法躲避) > 0)
                    {
                        if (succeeded) tempTo.AddedStats[Stat.魔法躲避] += tempFrom.GetTotal(Stat.魔法躲避);
                    }

                    else if (tempFrom.GetTotal(Stat.毒物躲避) > 0)
                    {
                        if (succeeded) tempTo.AddedStats[Stat.毒物躲避] += tempFrom.GetTotal(Stat.毒物躲避);
                    }
                    else if (tempFrom.GetTotal(Stat.幸运) > 0)
                    {
                        if (succeeded) tempTo.AddedStats[Stat.幸运] += tempFrom.GetTotal(Stat.幸运);
                    }
                    else
                    {
                        ReceiveChat("无法赋能这件物品", ChatType.Hint);
                        Enqueue(p);
                        return;
                    }

                    if (!succeeded)
                    {
                        if ((tempFrom.Info.Shape == 3) && (Envir.Random.Next(15) < 3))
                        {
                            //item destroyed
                            ReceiveChat("物品已销毁", ChatType.Hint);
                            Report.ItemChanged(array[indexTo], 1, 1, "组合物品 (物品销毁)");

                            array[indexTo] = null;
                            p.Destroy = true;
                        }
                        else
                        {
                            //upgrade has no effect
                            ReceiveChat("赋能失败", ChatType.Hint);
                        }

                        canUpgrade = false;
                    }
                    break;
                default:
                    Enqueue(p);
                    return;
            }


            switch (grid)
            {
                case MirGridType.Inventory:
                    RefreshBagWeight();
                    break;
                case MirGridType.HeroInventory:
                    Hero.RefreshBagWeight();
                    break;
            }

            if (canRepair && array[indexTo] != null)
            {
                switch (tempTo.Info.Shape)
                {
                    case 1:
                    case 2:
                        {
                            tempTo.MaxDura = (ushort)Math.Max(0, Math.Min(tempTo.MaxDura, tempTo.MaxDura - 100 * Envir.Random.Next(10)));
                        }
                        break;
                    default:
                        break;
                }
                tempTo.CurrentDura = tempTo.MaxDura;
                tempTo.DuraChanged = false;

                ReceiveChat("修复完成", ChatType.Hint);
                Enqueue(new S.ItemRepaired { UniqueID = tempTo.UniqueID, MaxDura = tempTo.MaxDura, CurrentDura = tempTo.CurrentDura });
            }

            if (canUpgrade && array[indexTo] != null)
            {
                tempTo.GemCount++;
                ReceiveChat("赋能成功", ChatType.Hint);
                Enqueue(new S.ItemUpgraded { Item = tempTo });
            }

            if (canSlotUpgrade && array[indexTo] != null)
            {
                tempTo.SetSlotSize(tempTo.Slots.Length + 1);
                ReceiveChat("嵌孔成功", ChatType.Hint);
                Enqueue(new S.ItemSlotSizeChanged { UniqueID = tempTo.UniqueID, SlotSize = tempTo.Slots.Length });
            }

            if (canSeal && array[indexTo] != null)
            {
                var minutes = tempFrom.CurrentDura;
                tempTo.SealedInfo = new SealedInfo 
                { 
                    ExpiryDate = Envir.Now.AddMinutes(minutes), 
                    NextSealDate = Envir.Now.AddMinutes(minutes).AddMinutes(Settings.ItemSealDelay) 
                };

                ReceiveChat($"锁定时间：{Functions.PrintTimeSpanFromSeconds(minutes * 60)}", ChatType.Hint);

                Enqueue(new S.ItemSealChanged { UniqueID = tempTo.UniqueID, ExpiryDate = tempTo.SealedInfo.ExpiryDate });
            }

            if (tempFrom.Count > 1) tempFrom.Count--;
            else array[indexFrom] = null;

            Report.ItemCombined(tempFrom, tempTo, indexFrom, indexTo, grid);

            //item merged ok
            TradeUnlock();

            p.Success = true;
            Enqueue(p);
        }
        private bool ValidGemForItem(UserItem Gem, byte itemtype)
        {
            switch (itemtype)
            {
                case 1: //weapon
                    if (Gem.Info.Unique.HasFlag(SpecialItemMode.Paralize))
                        return true;
                    break;
                case 2: //Armour
                    if (Gem.Info.Unique.HasFlag(SpecialItemMode.Teleport))
                        return true;
                    break;
                case 4: //Helmet
                    if (Gem.Info.Unique.HasFlag(SpecialItemMode.ClearRing))
                        return true;
                    break;
                case 5: //necklace
                    if (Gem.Info.Unique.HasFlag(SpecialItemMode.Protection))
                        return true;
                    break;
                case 6: //bracelet
                    if (Gem.Info.Unique.HasFlag(SpecialItemMode.Revival))
                        return true;
                    break;
                case 7: //ring
                    if (Gem.Info.Unique.HasFlag(SpecialItemMode.Muscle))
                        return true;
                    break;
                case 8: //amulet
                    if (Gem.Info.Unique.HasFlag(SpecialItemMode.Flame))
                        return true;
                    break;
                case 9://belt
                    if (Gem.Info.Unique.HasFlag(SpecialItemMode.Healing))
                        return true;
                    break;
                case 10: //boots
                    if (Gem.Info.Unique.HasFlag(SpecialItemMode.Probe))
                        return true;
                    break;
                case 11: //stone
                    if (Gem.Info.Unique.HasFlag(SpecialItemMode.Skill))
                        return true;
                    break;
                case 12:///torch
                    if (Gem.Info.Unique.HasFlag(SpecialItemMode.NoDuraLoss))
                        return true;
                    break;
            }
            return false;
        }
        //Gems granting multiple stat types are not compatiable with this method.
        private Stat GetGemType(UserItem gem)
        {
            if (gem.GetTotal(Stat.MaxDC) > 0)
                return Stat.MaxDC;

            else if (gem.GetTotal(Stat.MaxMC) > 0)
                return Stat.MaxMC;

            else if (gem.GetTotal(Stat.MaxSC) > 0)
                return Stat.MaxSC;

            else if (gem.GetTotal(Stat.MaxAC) > 0)
                return Stat.MaxAC;

            else if (gem.GetTotal(Stat.MaxMAC) > 0)
                return Stat.MaxMAC;

            else if (gem.GetTotal(Stat.攻击速度) > 0)
                return Stat.攻击速度;

            else if (gem.GetTotal(Stat.敏捷) > 0)
                return Stat.敏捷;

            else if (gem.GetTotal(Stat.准确) > 0)
                return Stat.准确;

            else if (gem.GetTotal(Stat.毒素伤害) > 0)
                return Stat.毒素伤害;

            else if (gem.GetTotal(Stat.冰冻伤害) > 0)
                return Stat.冰冻伤害;

            else if (gem.GetTotal(Stat.魔法躲避) > 0)
                return Stat.魔法躲避;

            else if (gem.GetTotal(Stat.毒物躲避) > 0)
                return Stat.毒物躲避;

            else if (gem.GetTotal(Stat.幸运) > 0)
                return Stat.幸运;

            else if (gem.GetTotal(Stat.中毒恢复) > 0)
                return Stat.中毒恢复;

            else if (gem.GetTotal(Stat.HP) > 0)
                return Stat.HP;

            else if (gem.GetTotal(Stat.MP) > 0)
                return Stat.MP;

            else if (gem.GetTotal(Stat.生命恢复) > 0)
                return Stat.生命恢复;

            // These may be incomplete. Item definitions may be missing?

            else if (gem.GetTotal(Stat.生命值数率) > 0)
                return Stat.生命值数率;

            else if (gem.GetTotal(Stat.法力值数率) > 0)
                return Stat.法力值数率;

            else if (gem.GetTotal(Stat.法力恢复) > 0)
                return Stat.法力恢复;

            else if (gem.GetTotal(Stat.神圣) > 0)
                return Stat.神圣;

            else if (gem.GetTotal(Stat.强度) > 0)
                return Stat.强度;

            else if (gem.GetTotal(Stat.吸血数率) > 0)
                return Stat.吸血数率;

            return Stat.Unknown;
        }
        //Gems granting multiple stat types are not compatible with this method.        
        public void DropItem(ulong id, ushort count, bool isHeroItem)
        {
            S.DropItem p = new S.DropItem { UniqueID = id, Count = count, HeroItem = isHeroItem, Success = false };
            if (Dead)
            {
                Enqueue(p);
                return;
            }

            if (CurrentMap.Info.NoThrowItem)
            {
                ReceiveChat(GameLanguage.CanNotDrop, ChatType.System);
                Enqueue(p);
                return;
            }

            UserItem temp = null;
            int index = -1;
            HeroObject currentHero = null;

            if (!isHeroItem)
            {
                for (int i = 0; i < Info.Inventory.Length; i++)
                {
                    temp = Info.Inventory[i];
                    if (temp == null || temp.UniqueID != id) continue;
                    index = i;
                    break;
                }
            }
            else
            {
                currentHero = Envir.Heroes.FirstOrDefault(h => h.Info.Index == Info.CurrentHeroIndex);

                if (currentHero != null)
                {
                    for (int i = 0; i < currentHero.Info.Inventory.Length; i++)
                    {
                        temp = currentHero.Info.Inventory[i];
                        if (temp == null || temp.UniqueID != id) continue;
                        index = i;
                        break;
                    }
                }
                else
                {
                    Enqueue(p);
                    return;
                }
            }

            if (temp == null || index == -1 || count > temp.Count || count < 1)
            {
                Enqueue(p);
                return;
            }

            if (temp.Info.Bind.HasFlag(BindMode.DontDrop))
            {
                Enqueue(p);
                return;
            }

            if (temp.RentalInformation != null && temp.RentalInformation.BindingFlags.HasFlag(BindMode.DontDrop))
            {
                Enqueue(p);
                return;
            }

            if (temp.Count == count)
            {
                if (!temp.Info.Bind.HasFlag(BindMode.DestroyOnDrop))
                    if (!DropItem(temp))
                    {
                        Enqueue(p);
                        return;
                    }

                if (p.HeroItem)
                {
                        currentHero.Info.Inventory[index] = null;
                }
                else
                {
                    Info.Inventory[index] = null;
                }
                
            }
            else
            {
                UserItem temp2 = Envir.CreateFreshItem(temp.Info);
                temp2.Count = count;
                if (!temp.Info.Bind.HasFlag(BindMode.DestroyOnDrop))
                    if (!DropItem(temp2))
                    {
                        Enqueue(p);
                        return;
                    }
                temp.Count -= count;
            }
            p.Success = true;
            Enqueue(p);

            if (p.HeroItem)
            {
                currentHero.RefreshBagWeight();
                currentHero.Report.ItemChangedHero(temp, count, 1);
            }
            else
            {
                RefreshBagWeight();
                Report.ItemChanged(temp, count, 1);
            }  
        }
        public void DropGold(uint gold)
        {
            if (Account.Gold < gold) return;

            ItemObject ob = new ItemObject(this, gold);

            if (!ob.Drop(5)) return;
            Account.Gold -= gold;
            Enqueue(new S.LoseGold { Gold = gold });
        }
        public void PickUp()
        {
            if (Dead)
            {
                //Send Fail
                return;
            }

            Cell cell = CurrentMap.GetCell(CurrentLocation);

            bool sendFail = false;

            for (int i = 0; i < cell.Objects.Count; i++)
            {
                MapObject ob = cell.Objects[i];

                if (ob.Race != ObjectType.Item) continue;

                if (ob.Owner != null && ob.Owner != this && !IsGroupMember(ob.Owner)) //Or Group member.
                {
                    sendFail = true;
                    continue;
                }
                ItemObject item = (ItemObject)ob;

                if (item.Item != null)
                {
                    if (!CanGainItem(item.Item)) continue;

                    if (item.Item.Info.ShowGroupPickup && IsGroupMember(this))
                        for (int j = 0; j < GroupMembers.Count; j++)
                            GroupMembers[j].ReceiveChat(Name + " 拾取: {" + item.Item.FriendlyName + "}",
                                ChatType.System);

                    GainItem(item.Item);

                    Report.ItemChanged(item.Item, item.Item.Count, 2);

                    CurrentMap.RemoveObject(ob);
                    ob.Despawn();

                    return;
                }

                if (!CanGainGold(item.Gold)) continue;

                GainGold(item.Gold);
                CurrentMap.RemoveObject(ob);
                ob.Despawn();
                return;
            }

            if (sendFail)
                ReceiveChat("无法拾取该物品", ChatType.System);

        }
        public void RequestMapInfo(int mapIndex)
        {
            var info = Envir.GetMapInfo(mapIndex);
            CheckMapInfo(info);
        }
        public void TeleportToNPC(uint objectID)
        {
            for (int i = 0; i < CurrentMap.NPCs.Count; i++)
            {
                NPCObject ob = CurrentMap.NPCs[i];
                if (ob.ObjectID != objectID) continue;

                if (!ob.Info.CanTeleportTo) return;

                uint cost = (uint)Settings.TeleportToNPCCost;
                if (Account.Gold < cost) return;

                Point p = ob.Front;
                if (!CurrentMap.ValidPoint(p))
                {
                    for (int j = 0; j < 7; j++)
                    {
                        p = Functions.PointMove(CurrentLocation, Functions.ShiftDirection(ob.Direction, j), 1);
                        if (CurrentMap.ValidPoint(p)) break;
                    }
                }

                if (CurrentMap.ValidPoint(p))
                {
                    Account.Gold -= cost;
                    Enqueue(new S.LoseGold { Gold = cost });
                    Teleport(CurrentMap, p);
                }

                break;
            }
        }
        public void SearchMap(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length < 3) return;

            S.SearchMapResult p = new S.SearchMapResult();

            Map map = Envir.GetWorldMap(text);
            if (map != null)
            {
                CheckMapInfo(map.Info);
                p.MapIndex = map.Info.Index;
                Enqueue(p);
                return;
            }

            NPCObject npc = Envir.GetWorldMapNPC(text);
            if (npc != null)
            {
                CheckMapInfo(npc.CurrentMap.Info);
                p.MapIndex = npc.CurrentMap.Info.Index;
                p.NPCIndex = npc.ObjectID;
                Enqueue(p);
                return;
            }

            Enqueue(p);
            return;
        }
        private bool IsGroupMember(MapObject player)
        {
            if (player.Race != ObjectType.Player) return false;
            return GroupMembers != null && GroupMembers.Contains(player);
        }
        public override bool CanGainGold(uint gold)
        {
            return (ulong)gold + Account.Gold <= uint.MaxValue;
        }
        public override void WinGold(uint gold)
        {
            if (GroupMembers == null)
            {
                GainGold(gold);
                return;
            }

            uint count = 0;

            for (int i = 0; i < GroupMembers.Count; i++)
            {
                PlayerObject player = GroupMembers[i];
                if (player.CurrentMap == CurrentMap && Functions.InRange(player.CurrentLocation, CurrentLocation, Globals.DataRange) && !player.Dead)
                    count++;
            }

            if (count == 0 || count > gold)
            {
                GainGold(gold);
                return;
            }
            gold = gold / count;

            for (int i = 0; i < GroupMembers.Count; i++)
            {
                PlayerObject player = GroupMembers[i];
                if (player.CurrentMap == CurrentMap && Functions.InRange(player.CurrentLocation, CurrentLocation, Globals.DataRange) && !player.Dead)
                    player.GainGold(gold);
            }
        }
        public void GainGold(uint gold)
        {
            if (gold == 0) return;

            if (((UInt64)Account.Gold + gold) > uint.MaxValue)
                gold = uint.MaxValue - Account.Gold;

            Account.Gold += gold;

            Enqueue(new S.GainedGold { Gold = gold });
        }
        public void GainCredit(uint credit)
        {
            if (credit == 0) return;

            if (((UInt64)Account.Credit + credit) > uint.MaxValue)
                credit = uint.MaxValue - Account.Credit;

            Account.Credit += credit;

            Enqueue(new S.GainedCredit { Credit = credit });
        }
        public void GainItemMail(UserItem item, int reason)
        {
            Envir.MailCharacter(Info, item: item, reason: reason);
        }                 
        public bool CanRemoveItem(MirGridType grid, UserItem item)
        {
            //Item  Stuck

            UserItem[] array;
            switch (grid)
            {
                case MirGridType.Inventory:
                    array = Info.Inventory;
                    break;
                case MirGridType.Storage:
                    array = Account.Storage;
                    break;
                case MirGridType.HeroInventory:
                    array = CurrentHero.Inventory;
                    break;
                default:
                    return false;
            }

            if (RidingMount && item.Info.Type != ItemType.照明物)
            {
                return false;
            }

            return FreeSpace(array) > 0;
        }
        public bool CheckQuestItem(UserItem uItem, ushort count)
        {
            foreach (var item in Info.QuestInventory.Where(item => item != null && item.Info == uItem.Info))
            {
                if (count > item.Count)
                {
                    count -= item.Count;
                    continue;
                }

                if (count > item.Count) continue;
                count = 0;
                break;
            }

            return count <= 0;
        }
        public bool CanGainQuestItem(UserItem item)
        {
            if (FreeSpace(Info.QuestInventory) > 0) return true;

            if (item.Info.StackSize > 1)
            {
                ushort count = item.Count;

                for (int i = 0; i < Info.QuestInventory.Length; i++)
                {
                    UserItem bagItem = Info.QuestInventory[i];

                    if (bagItem.Info != item.Info) continue;

                    if (bagItem.Count + count <= bagItem.Info.StackSize) return true;

                    count -= (ushort)(bagItem.Info.StackSize - bagItem.Count);
                }
            }

            ReceiveChat("不能再携带任务物品", ChatType.System);

            return false;
        }
        public void GainQuestItem(UserItem item)
        {
            CheckItem(item);

            UserItem clonedItem = item.Clone();

            Enqueue(new S.GainedQuestItem { Item = clonedItem });

            AddQuestItem(item);
        }
        public void TakeQuestItem(ItemInfo uItem, ushort count)
        {
            for (int o = 0; o < Info.QuestInventory.Length; o++)
            {
                UserItem item = Info.QuestInventory[o];
                if (item == null) continue;
                if (item.Info != uItem) continue;

                if (count > item.Count)
                {
                    Enqueue(new S.DeleteQuestItem { UniqueID = item.UniqueID, Count = item.Count });
                    Info.QuestInventory[o] = null;

                    count -= item.Count;
                    continue;
                }

                Enqueue(new S.DeleteQuestItem { UniqueID = item.UniqueID, Count = count });

                if (count == item.Count)
                    Info.QuestInventory[o] = null;
                else
                    item.Count -= count;
                break;
            }
        }       
        
        public void RequestChatItem(ulong id)
        {
            //Enqueue(new S.ChatItemStats { ChatItemId = id, Stats = whatever });
        }
        
        public override void ReceiveChat(string text, ChatType type)
        {
            Enqueue(new S.Chat { Message = text, Type = type });
        }
        public void ReceiveOutputMessage(string text, OutputMessageType type)
        {
            Enqueue(new S.SendOutputMessage { Message = text, Type = type });
        }                
        public void Opendoor(byte Doorindex)
        {
            //todo: add check for sw doors
            if (CurrentMap.OpenDoor(Doorindex))
            {
                Enqueue(new S.Opendoor() { DoorIndex = Doorindex });
                Broadcast(new S.Opendoor() { DoorIndex = Doorindex });
            }
        }

        #region NPC

        public void CallDefaultNPC(DefaultNPCType type, params object[] value)
        {
            string key = string.Empty;

            switch (type)
            {
                case DefaultNPCType.Login:
                    key = "Login";
                    break;
                case DefaultNPCType.UseItem:
                    if (value.Length < 1) return;
                    key = string.Format("UseItem({0})", value[0]);
                    break;
                case DefaultNPCType.Trigger:
                    if (value.Length < 1) return;
                    key = string.Format("Trigger({0})", value[0]);
                    break;
                case DefaultNPCType.MapCoord:
                    if (value.Length < 3) return;
                    key = string.Format("MapCoord({0},{1},{2})", value[0], value[1], value[2]);
                    break;
                case DefaultNPCType.MapEnter:
                    if (value.Length < 1) return;
                    key = string.Format("MapEnter({0})", value[0]);
                    break;
                case DefaultNPCType.Die:
                    key = "Die";
                    break;
                case DefaultNPCType.LevelUp:
                    key = "LevelUp";
                    break;
                case DefaultNPCType.CustomCommand:
                    if (value.Length < 1) return;
                    key = string.Format("CustomCommand({0})", value[0]);
                    break;
                case DefaultNPCType.OnAcceptQuest:
                    if (value.Length < 1) return;
                    key = string.Format("OnAcceptQuest({0})", value[0]);
                    break;
                case DefaultNPCType.OnFinishQuest:
                    if (value.Length < 1) return;
                    key = string.Format("OnFinishQuest({0})", value[0]);
                    break;
                case DefaultNPCType.Daily:
                    key = "Daily";
                    Info.NewDay = false;
                    break;
                case DefaultNPCType.Client:
                    key = "Client";
                    break;
            }

            key = string.Format("[@_{0}]", key);

            DelayedAction action = new DelayedAction(DelayedType.NPC, Envir.Time, Envir.DefaultNPC.LoadedObjectID, Envir.DefaultNPC.ScriptID, key);
            ActionList.Add(action);

            Enqueue(new S.NPCUpdate { NPCID = Envir.DefaultNPC.LoadedObjectID });
        }

        public void CallDefaultNPC(string key)
        {
            if (NPCObjectID != Envir.DefaultNPC.LoadedObjectID) return;

            var script = NPCScript.Get(NPCScriptID);
            script.Call(this, NPCObjectID, key.ToUpper());

            CallNPCNextPage();
        }

        public void CallNPC(uint objectID, string key)
        {
            if (Dead) return;

            key = key.ToUpper();

            for (int i = 0; i < CurrentMap.NPCs.Count; i++)
            {
                NPCObject ob = CurrentMap.NPCs[i];
                if (ob.ObjectID != objectID) continue;
                if (!Functions.InRange(ob.CurrentLocation, CurrentLocation, Globals.DataRange)) return;

                ob.CheckVisible(this);

                if (!ob.VisibleLog[Info.Index] || !ob.Visible) return;

                var scriptID = NPCScriptID;
                if (objectID != NPCObjectID || key == NPCScript.MainKey)
                {
                    scriptID = ob.ScriptID;
                }

                var script = NPCScript.Get(scriptID);
                script.Call(this, objectID, key);

                break;
            }

            CallNPCNextPage();
        }
        private void CallNPCNextPage()
        {
            //process any new npc calls immediately
            for (int i = 0; i < ActionList.Count; i++)
            {
                if (ActionList[i].Type != DelayedType.NPC || ActionList[i].Time != -1) continue;
                var action = ActionList[i];

                ActionList.RemoveAt(i);

                CompleteNPC(action.Params);
            }
        }

        public void BuyItem(ulong index, ushort count, PanelType type)
        {
            if (Dead || count < 1) return;

            if (NPCPage == null ||
                !(String.Equals(NPCPage.Key, NPCScript.BuySellKey, StringComparison.CurrentCultureIgnoreCase) ||
                String.Equals(NPCPage.Key, NPCScript.BuyKey, StringComparison.CurrentCultureIgnoreCase) ||
                String.Equals(NPCPage.Key, NPCScript.BuyBackKey, StringComparison.CurrentCultureIgnoreCase) ||
                String.Equals(NPCPage.Key, NPCScript.BuyUsedKey, StringComparison.CurrentCultureIgnoreCase) ||
                String.Equals(NPCPage.Key, NPCScript.PearlBuyKey, StringComparison.CurrentCultureIgnoreCase) ||
                String.Equals(NPCPage.Key, NPCScript.BuyNewKey, StringComparison.CurrentCultureIgnoreCase) ||
                String.Equals(NPCPage.Key, NPCScript.BuySellNewKey, StringComparison.CurrentCultureIgnoreCase))) return;

            for (int i = 0; i < CurrentMap.NPCs.Count; i++)
            {
                NPCObject ob = CurrentMap.NPCs[i];
                if (ob.ObjectID != NPCObjectID) continue;
                if (!Functions.InRange(ob.CurrentLocation, CurrentLocation, Globals.DataRange)) return;

                if (type == PanelType.Buy)
                {
                    NPCScript script = NPCScript.Get(NPCScriptID);
                    script.Buy(this, index, count);
                }
            }
        }
        public void CraftItem(ulong index, ushort count, int[] slots)
        {
            if (Dead || count < 1) return;

            if (NPCPage == null) return;

            for (int i = 0; i < CurrentMap.NPCs.Count; i++)
            {
                NPCObject ob = CurrentMap.NPCs[i];
                if (ob.ObjectID != NPCObjectID) continue;
                if (!Functions.InRange(ob.CurrentLocation, CurrentLocation, Globals.DataRange)) return;

                NPCScript script = NPCScript.Get(NPCScriptID);
                script.Craft(this, index, count, slots);
            }
        }


        public void SellItem(ulong uniqueID, ushort count)
        {
            S.SellItem p = new S.SellItem { UniqueID = uniqueID, Count = count };

            if (Dead || count == 0)
            {
                Enqueue(p);
                return;
            }

            if (NPCPage == null || !(String.Equals(NPCPage.Key, NPCScript.BuySellKey, StringComparison.CurrentCultureIgnoreCase) || String.Equals(NPCPage.Key, NPCScript.SellKey, StringComparison.CurrentCultureIgnoreCase)))
            {
                Enqueue(p);
                return;
            }

            for (int n = 0; n < CurrentMap.NPCs.Count; n++)
            {
                NPCObject ob = CurrentMap.NPCs[n];
                if (ob.ObjectID != NPCObjectID) continue;
                if (!Functions.InRange(ob.CurrentLocation, CurrentLocation, Globals.DataRange)) return;

                UserItem temp = null;
                int index = -1;

                for (int i = 0; i < Info.Inventory.Length; i++)
                {
                    temp = Info.Inventory[i];
                    if (temp == null || temp.UniqueID != uniqueID) continue;
                    index = i;
                    break;
                }

                if (temp == null || index == -1 || count > temp.Count)
                {
                    Enqueue(p);
                    return;
                }

                if (temp.Info.Bind.HasFlag(BindMode.DontSell))
                {
                    Enqueue(p);
                    return;
                }

                if (temp.RentalInformation != null && temp.RentalInformation.BindingFlags.HasFlag(BindMode.DontSell))
                {
                    Enqueue(p);
                    return;
                }

                NPCScript script = NPCScript.Get(NPCScriptID);

                if (script.Types.Count != 0 && !script.Types.Contains(temp.Info.Type))
                {
                    ReceiveChat("该物品不能在此出售", ChatType.System);
                    Enqueue(p);
                    return;
                }

                if (temp.Info.StackSize > 1 && count != temp.Count)
                {
                    UserItem item = Envir.CreateFreshItem(temp.Info);
                    item.Count = count;

                    if (item.Price() / 2 + Account.Gold > uint.MaxValue)
                    {
                        Enqueue(p);
                        return;
                    }

                    temp.Count -= count;
                    temp = item;
                }
                else Info.Inventory[index] = null;

                script.Sell(this, temp);

                if (Settings.GoodsOn)
                {
                    var callingNPC = NPCObject.Get(NPCObjectID);

                    if (callingNPC != null)
                    {
                        if (!callingNPC.BuyBack.ContainsKey(Name)) callingNPC.BuyBack[Name] = new List<UserItem>();

                        if (Settings.GoodsBuyBackMaxStored > 0 && callingNPC.BuyBack[Name].Count >= Settings.GoodsBuyBackMaxStored)
                            callingNPC.BuyBack[Name].RemoveAt(0);

                        temp.BuybackExpiryDate = Envir.Now;
                        callingNPC.BuyBack[Name].Add(temp);
                    }
                }

                p.Success = true;
                Enqueue(p);
                GainGold(temp.Price() / 2);
                RefreshBagWeight();

                return;
            }

            Enqueue(p);
        }
        public void RepairItem(ulong uniqueID, bool special = false)
        {
            Enqueue(new S.RepairItem { UniqueID = uniqueID });

            if (Dead) return;

            if (NPCPage == null || (!String.Equals(NPCPage.Key, NPCScript.RepairKey, StringComparison.CurrentCultureIgnoreCase) && !special) || (!String.Equals(NPCPage.Key, NPCScript.SRepairKey, StringComparison.CurrentCultureIgnoreCase) && special)) return;

            for (int n = 0; n < CurrentMap.NPCs.Count; n++)
            {
                NPCObject ob = CurrentMap.NPCs[n];
                if (ob.ObjectID != NPCObjectID) continue;
                if (!Functions.InRange(ob.CurrentLocation, CurrentLocation, Globals.DataRange)) return;

                UserItem temp = null;
                int index = -1;

                for (int i = 0; i < Info.Inventory.Length; i++)
                {
                    temp = Info.Inventory[i];
                    if (temp == null || temp.UniqueID != uniqueID) continue;
                    index = i;
                    break;
                }

                if (temp == null || index == -1) return;

                if ((temp.Info.Bind.HasFlag(BindMode.DontRepair)) || (temp.Info.Bind.HasFlag(BindMode.NoSRepair) && special))
                {
                    ReceiveChat("无法修复该物品", ChatType.System);
                    return;
                }

                NPCScript script = NPCScript.Get(NPCScriptID);

                if (script.Types.Count != 0 && !script.Types.Contains(temp.Info.Type))
                {
                    ReceiveChat("此处无法修复该物品", ChatType.System);
                    return;
                }

                uint cost;
                uint baseCost;
                if (!special)
                {
                    cost = (uint)(temp.RepairPrice() * script.PriceRate(this));
                    baseCost = (uint)(temp.RepairPrice() * script.PriceRate(this, true));
                }
                else
                {
                    cost = (uint)(temp.RepairPrice() * 3 * script.PriceRate(this));
                    baseCost = (uint)(temp.RepairPrice() * 3 * script.PriceRate(this, true));
                }

                if (cost > Account.Gold) return;

                Account.Gold -= cost;
                Enqueue(new S.LoseGold { Gold = cost });
                if (ob.Conq != null) ob.Conq.GuildInfo.GoldStorage += (cost - baseCost);

                if (!special) temp.MaxDura = (ushort)Math.Max(0, temp.MaxDura - (temp.MaxDura - temp.CurrentDura) / 30);

                temp.CurrentDura = temp.MaxDura;
                temp.DuraChanged = false;

                Enqueue(new S.ItemRepaired { UniqueID = uniqueID, MaxDura = temp.MaxDura, CurrentDura = temp.CurrentDura });
                return;
            }
        }
        public void SendStorage()
        {
            if (Connection.StorageSent) return;
            Connection.StorageSent = true;

            for (int i = 0; i < Account.Storage.Length; i++)
            {
                UserItem item = Account.Storage[i];
                if (item == null) continue;
                //CheckItemInfo(item.Info);
                CheckItem(item);
            }

            Enqueue(new S.UserStorage { Storage = Account.Storage }); // Should be no alter before being sent.
        }

        #endregion

        #region Consignment
        public void ConsignItem(ulong uniqueID, uint price, MarketPanelType panelType)
        {
            S.ConsignItem p = new S.ConsignItem { UniqueID = uniqueID };

            if (Dead || NPCPage == null)
            {
                Enqueue(p);
                return;
            }

            switch (panelType)
            {
                case MarketPanelType.Consign:
                    {
                        if (price < Globals.MinConsignment || price > Globals.MaxConsignment)
                        {
                            Enqueue(p);
                            return;
                        }

                        if (Account.Gold < Globals.ConsignmentCost)
                        {
                            Enqueue(p);
                            return;
                        }
                    }
                    break;
                case MarketPanelType.Auction:
                    {
                        if (price < Globals.MinStartingBid || price > Globals.MaxStartingBid)
                        {
                            Enqueue(p);
                            return;
                        }

                        if (Account.Gold < Globals.AuctionCost)
                        {
                            Enqueue(p);
                            return;
                        }
                    }
                    break;
                default:
                    Enqueue(p);
                    return;
            }

            for (int n = 0; n < CurrentMap.NPCs.Count; n++)
            {
                NPCObject ob = CurrentMap.NPCs[n];
                if (ob.ObjectID != NPCObjectID) continue;
                if (!Functions.InRange(ob.CurrentLocation, CurrentLocation, Globals.DataRange)) return;

                UserItem temp = null;
                int index = -1;

                for (int i = 0; i < Info.Inventory.Length; i++)
                {
                    temp = Info.Inventory[i];
                    if (temp == null || temp.UniqueID != uniqueID) continue;
                    index = i;
                    break;
                }

                if (temp == null || index == -1)
                {
                    Enqueue(p);
                    return;
                }

                if (temp.Info.Bind.HasFlag(BindMode.DontSell))
                {
                    Enqueue(p);
                    return;
                }

                MarketItemType type = panelType == MarketPanelType.Consign ? MarketItemType.Consign : MarketItemType.Auction;
                uint cost = panelType == MarketPanelType.Consign ? Globals.ConsignmentCost : Globals.AuctionCost;

                //TODO Check Max Consignment.

                AuctionInfo auction = new AuctionInfo(Info, temp, price, type);

                Account.Auctions.AddLast(auction);
                Envir.Auctions.AddFirst(auction);

                p.Success = true;
                Enqueue(p);

                Info.Inventory[index] = null;

                Account.Gold -= cost;

                Enqueue(new S.LoseGold { Gold = cost });
                RefreshBagWeight();
            }

            Enqueue(p);
        }

        private bool Match(AuctionInfo info)
        {
            return (UserMatch || !info.Expired && !info.Sold)
                && (!UserMatch || ((MarketPanelType == MarketPanelType.Auction && info.ItemType == MarketItemType.Auction) || (MarketPanelType != MarketPanelType.Auction && info.ItemType == MarketItemType.Consign)))
                && ((MatchType == ItemType.杂物 || info.Item.Info.Type == MatchType)
                && (info.Item.Info.Shape >= MinShapes && info.Item.Info.Shape <= MaxShapes)
                && (string.IsNullOrWhiteSpace(MatchName) || info.Item.Info.Name.Replace(" ", "").IndexOf(MatchName, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        public void MarketPage(int page)
        {
            if (Dead || Envir.Time < SearchTime) return;

            if (MarketPanelType != MarketPanelType.GameShop)
            {
                bool failed = true;

                if (NPCPage == null || (!String.Equals(NPCPage.Key, NPCScript.MarketKey, StringComparison.CurrentCultureIgnoreCase)) || page <= PageSent) return;

                SearchTime = Envir.Time + Globals.SearchDelay;

                for (int n = 0; n < CurrentMap.NPCs.Count; n++)
                {
                    NPCObject ob = CurrentMap.NPCs[n];
                    if (ob.ObjectID != NPCObjectID) continue;
                    if (!Functions.InRange(ob.CurrentLocation, CurrentLocation, Globals.DataRange)) return;
                    failed = false;
                }

                if (failed)
                {
                    return;
                }
            }

            List<AuctionInfo> listings = new List<AuctionInfo>();
            List<ClientAuction> clientListings = new List<ClientAuction>();

            for (int i = 0; i < 10; i++)
            {
                if (i + page * 10 >= Search.Count) break;
                listings.Add(Search[i + page * 10]);
            }

            foreach (var listing in listings)
            {
                clientListings.Add(listing.CreateClientAuction(UserMatch));
            }

            for (int i = 0; i < listings.Count; i++)
            {
                CheckItem(listings[i].Item);
            }

            PageSent = page;
            Enqueue(new S.NPCMarketPage { Listings = clientListings });
        }

        public void GetMarket(string name, ItemType type)
        {
            Search.Clear();
            MatchName = name.Replace(" ", "");
            MatchType = type;
            PageSent = 0;

            long start = Envir.Stopwatch.ElapsedMilliseconds;

            if (MarketPanelType == MarketPanelType.GameShop)
            {
                //Search = Envir.GameShopList.Where(x => (MatchType == ItemType.Nothing || x.Info.Type == MatchType)
                //&& (x.Info.Shape >= MinShapes && x.Info.Shape <= MaxShapes)
                //&& (string.IsNullOrWhiteSpace(MatchName) || x.Info.Name.Replace(" ", "").IndexOf(MatchName, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            }
            else
            {
                LinkedListNode<AuctionInfo> current = UserMatch ? Account.Auctions.First : Envir.Auctions.First;

                while (current != null)
                {
                    if (Match(current.Value)) Search.Add(current.Value);
                    current = current.Next;
                }
            }

            List<AuctionInfo> listings = new List<AuctionInfo>();
            List<ClientAuction> clientListings = new List<ClientAuction>();

            for (int i = 0; i < 10; i++)
            {
                if (i >= Search.Count) break;
                listings.Add(Search[i]);
            }

            foreach (var listing in listings)
            {
                clientListings.Add(listing.CreateClientAuction(UserMatch));
            }

            for (int i = 0; i < listings.Count; i++)
                CheckItem(listings[i].Item);

            Enqueue(new S.NPCMarket { Listings = clientListings, Pages = (Search.Count - 1) / 10 + 1, UserMode = UserMatch });

            MessageQueue.EnqueueDebugging(string.Format("{0}ms 匹配 {1} 物品", Envir.Stopwatch.ElapsedMilliseconds - start, MarketPanelType == MarketPanelType.GameShop ? Envir.GameShopList.Count : (UserMatch ? Account.Auctions.Count : Envir.Auctions.Count)));
        }

        public void MarketSearch(string match, ItemType type)
        {
            if (Dead || Envir.Time < SearchTime) return;

            SearchTime = Envir.Time + Globals.SearchDelay;

            if (MarketPanelType == MarketPanelType.GameShop)
            {
                GetMarket(match, type);
            }
            else
            {
                if (NPCPage == null || !String.Equals(NPCPage.Key, NPCScript.MarketKey, StringComparison.CurrentCultureIgnoreCase)) return;

                for (int n = 0; n < CurrentMap.NPCs.Count; n++)
                {
                    NPCObject ob = CurrentMap.NPCs[n];
                    if (ob.ObjectID != NPCObjectID) continue;

                    if (!Functions.InRange(CurrentLocation, ob.CurrentLocation, Globals.DataRange)) return;

                    GetMarket(match, type);
                }
            }
        }

        public void MarketBuy(ulong auctionID, uint bidPrice = 0)
        {
            if (Dead)
            {
                Enqueue(new S.MarketFail { Reason = 0 });
                return;
            }

            if (MarketPanelType == MarketPanelType.GameShop)
            {
                foreach (AuctionInfo auction in Search)
                {
                    if (auction.AuctionID != auctionID) continue;
                    if (auction.ItemType != MarketItemType.GameShop) continue;

                    if (auction.Price > Account.Credit)
                    {
                        Enqueue(new S.MarketFail { Reason = 4 });
                        return;
                    }

                    if (!CanGainItem(auction.Item))
                    {
                        Enqueue(new S.MarketFail { Reason = 5 });
                        return;
                    }

                    UserItem item = Envir.CreateFreshItem(auction.Item.Info);

                    Account.Credit -= auction.Price;
                    GainItem(item);
                    Enqueue(new S.MarketSuccess { Message = string.Format("购买 {0} 消费 {1:#,##0} 信用币", auction.Item.FriendlyName, auction.Price) });
                    MarketSearch(MatchName, MatchType);

                    return;
                }
            }
            else
            {
                if (NPCPage == null || !String.Equals(NPCPage.Key, NPCScript.MarketKey, StringComparison.CurrentCultureIgnoreCase))
                {
                    Enqueue(new S.MarketFail { Reason = 1 });
                    return;
                }

                for (int n = 0; n < CurrentMap.NPCs.Count; n++)
                {
                    NPCObject ob = CurrentMap.NPCs[n];
                    if (ob.ObjectID != NPCObjectID) continue;

                    if (!Functions.InRange(ob.CurrentLocation, CurrentLocation, Globals.DataRange)) return;

                    foreach (AuctionInfo auction in Search)
                    {
                        if (auction.AuctionID != auctionID) continue;
                        if (auction.ItemType != MarketItemType.Consign && auction.ItemType != MarketItemType.Auction) continue;

                        if (auction.Sold)
                        {
                            Enqueue(new S.MarketFail { Reason = 2 });
                            return;
                        }

                        if (auction.Expired)
                        {
                            Enqueue(new S.MarketFail { Reason = 3 });
                            return;
                        }

                        if (!Envir.Auctions.Contains(auction))
                        {
                            Enqueue(new S.MarketFail { Reason = 3 });
                            return;
                        }

                        if (!CanGainItem(auction.Item))
                        {
                            Enqueue(new S.MarketFail { Reason = 5 });
                            return;
                        }

                        if (Account.Auctions.Contains(auction))
                        {
                            Enqueue(new S.MarketFail { Reason = 6 });
                            return;
                        }

                        if (auction.Price > Account.Gold || bidPrice > Account.Gold)
                        {
                            Enqueue(new S.MarketFail { Reason = 4 });
                            return;
                        }

                        if (auction.ItemType == MarketItemType.Consign)
                        {
                            auction.Sold = true;

                            Account.Gold -= auction.Price;

                            Enqueue(new S.LoseGold { Gold = auction.Price });
                            GainItem(auction.Item);

                            Envir.MessageAccount(auction.SellerInfo.AccountInfo, string.Format("{0} 卖出价格: {1:#,##0} 金币", auction.Item.FriendlyName, auction.Price), ChatType.Hint);
                            Enqueue(new S.MarketSuccess { Message = string.Format("{0} 已购买并支付: {1:#,##0} 金币", auction.Item.FriendlyName, auction.Price) });
                            MarketSearch(MatchName, MatchType);
                        }
                        else
                        {
                            if (auction.CurrentBid > bidPrice)
                            {
                                Enqueue(new S.MarketFail { Reason = 9 });
                                return;
                            }

                            if (auction.CurrentBuyerInfo != null)
                            {
                                string message = string.Format("{0}竞价被超越 返回定金: {1:#,##0}金币", auction.Item.FriendlyName, auction.CurrentBid);

                                Envir.MailCharacter(auction.CurrentBuyerInfo, gold: auction.CurrentBid, customMessage: message);
                            }

                            auction.CurrentBid = bidPrice;
                            auction.CurrentBuyerIndex = Info.Index;
                            auction.CurrentBuyerInfo = Info;

                            Account.Gold -= bidPrice;
                            Enqueue(new S.LoseGold { Gold = bidPrice });

                            Envir.MessageAccount(auction.SellerInfo.AccountInfo, string.Format("{0} 当前竞标价格: {1:#,##0} 金币", auction.Item.FriendlyName, auction.CurrentBid), ChatType.Hint);
                            Enqueue(new S.MarketSuccess { Message = string.Format("花费 {1:#,##0} 金币 竞买: {0}", auction.Item.FriendlyName, auction.CurrentBid) });
                            MarketSearch(MatchName, MatchType);
                        }

                        return;
                    }
                }
            }

            Enqueue(new S.MarketFail { Reason = 7 });
        }

        public void MarketSellNow(ulong auctionID)
        {
            if (Dead)
            {
                Enqueue(new S.MarketFail { Reason = 0 });
                return;
            }

            if (NPCPage == null || !String.Equals(NPCPage.Key, NPCScript.MarketKey, StringComparison.CurrentCultureIgnoreCase))
            {
                Enqueue(new S.MarketFail { Reason = 1 });
                return;
            }

            for (int n = 0; n < CurrentMap.NPCs.Count; n++)
            {
                NPCObject ob = CurrentMap.NPCs[n];
                if (ob.ObjectID != NPCObjectID) continue;
                if (!Functions.InRange(ob.CurrentLocation, CurrentLocation, Globals.DataRange)) return;

                foreach (AuctionInfo auction in Account.Auctions)
                {
                    if (auction.AuctionID != auctionID) continue;

                    if (auction.ItemType != MarketItemType.Auction)
                    {
                        return;
                    }

                    if (auction.CurrentBid <= auction.Price || auction.CurrentBuyerInfo == null)
                    {
                        Enqueue(new S.MarketFail { Reason = 9 });
                        return;
                    }

                    if (auction.Sold && auction.Expired)
                    {
                        MessageQueue.Enqueue(string.Format("{0}过期或已拍卖", Account.AccountID));
                        return;
                    }

                    if (auction.Expired || auction.Sold || Envir.Now >= auction.ConsignmentDate.AddDays(Globals.ConsignmentLength))
                    {
                        Enqueue(new S.MarketFail { Reason = 10 });
                        return;
                    }

                    uint cost = auction.CurrentBid;

                    uint gold = (uint)Math.Max(0, cost - cost * Globals.Commission);

                    if (!CanGainGold(auction.CurrentBid))
                    {
                        Enqueue(new S.MarketFail { Reason = 8 });
                        return;
                    }

                    auction.Sold = true;

                    string message = string.Format("{0}竞拍成功 支付: {1:#,##0}金币", auction.Item.FriendlyName, auction.CurrentBid);

                    Envir.MailCharacter(auction.CurrentBuyerInfo, item: auction.Item, customMessage: message);
                    Envir.MessageAccount(auction.CurrentBuyerInfo.AccountInfo, string.Format("购买 {0} 支付: {1:#,##0}金币", auction.Item.FriendlyName, auction.CurrentBid), ChatType.Hint);

                    Account.Auctions.Remove(auction);
                    Envir.Auctions.Remove(auction);
                    GainGold(gold);
                    Enqueue(new S.MarketSuccess { Message = string.Format("{0}卖出价格: {1:#,##0}金币 \n收入: {2:#,##0}金币\n佣金: {3:#,##0}金币‎", auction.Item.FriendlyName, cost, gold, cost - gold) });
                    MarketSearch(MatchName, MatchType);
                    return;
                }

            }

            Enqueue(new S.MarketFail { Reason = 7 });
        }

        public void MarketGetBack(int mode, ulong auctionID)
        {
            AuctionInfo GetAuction(ulong auctionID)
            {
                foreach (var auction in Account.Auctions)
                {
                    if (auction.AuctionID == auctionID)
                        return auction;
                }
                return null;
            }

            bool TakeAuction(AuctionInfo auction)
            {
                if (auction.Sold && auction.Expired)
                {
                    MessageQueue.Enqueue(string.Format("拍卖已售出且已过期 {0}", Account.AccountID));
                    return false;
                }

                if (!auction.Sold || auction.Expired)
                {
                    if (!CanGainItem(auction.Item))
                    {
                        Enqueue(new S.MarketFail { Reason = 5 });
                        return false;
                    }

                    if (auction.CurrentBuyerInfo != null)
                    {
                        string message = string.Format("在对 {0} 的竞拍中已被超越。现退还 {1:#,##0} 金币", auction.Item.FriendlyName, auction.CurrentBid);

                        Envir.MailCharacter(auction.CurrentBuyerInfo, gold: auction.CurrentBid, customMessage: message);
                    }

                    GainItem(auction.Item);
                    return true;
                }

                if (mode == 2)
                    return false;

                uint cost = auction.ItemType == MarketItemType.Consign ? auction.Price : auction.CurrentBid;

                if (!CanGainGold(cost))
                {
                    Enqueue(new S.MarketFail { Reason = 8 });
                    return false;
                }

                uint gold = (uint)Math.Max(0, cost - cost * Globals.Commission);

                GainGold(gold);
                Enqueue(new S.MarketSuccess { Message = string.Format("您以 {1:#,##0} 金币的价格出售了 {0}。\n收益：{2:#,##0} 金币。\n佣金：{3:#,##0} 金币", auction.Item.FriendlyName, cost, gold, cost - gold) });
                return true;
            }


            if (Dead)
            {
                Enqueue(new S.MarketFail { Reason = 0 });
                return;
            }

            if (NPCPage == null || !String.Equals(NPCPage.Key, NPCScript.MarketKey, StringComparison.CurrentCultureIgnoreCase))
            {
                Enqueue(new S.MarketFail { Reason = 1 });
                return;
            }

            for (int n = 0; n < CurrentMap.NPCs.Count; n++)
            {
                NPCObject ob = CurrentMap.NPCs[n];
                if (ob.ObjectID != NPCObjectID) continue;
                if (!Functions.InRange(ob.CurrentLocation, CurrentLocation, Globals.DataRange)) return;

                if (mode == 0)
                {
                    var auction = GetAuction(auctionID);
                    if (auction != null)
                    {
                        if (TakeAuction(auction))
                        {
                            Account.Auctions.Remove(auction);
                            Envir.Auctions.Remove(auction);
                            MarketSearch(MatchName, MatchType);
                            return;
                        }
                    }
                }
                else
                {
                    int count = 0;
                    var node = Account.Auctions.First;
                    while (node != null)
                    {
                        var next = node.Next;

                        var auction = node.Value;
                        if (auction != null)
                        {
                            if (TakeAuction(auction))
                            {
                                Account.Auctions.Remove(node);
                                Envir.Auctions.Remove(auction);
                                count++;
                            }
                        }
                        node = next;
                    }

                    if (count > 0)
                    {
                        MarketSearch(MatchName, MatchType);
                        return;
                    }
                }
            }

            Enqueue(new S.MarketFail { Reason = 7 });
        }

        public void RequestUserName(uint id)
        {
            CharacterInfo Character = Envir.GetCharacterInfo((int)id);
            if (Character != null)
                Enqueue(new S.UserName { Id = (uint)Character.Index, Name = Character.Name });
        }

        #endregion

        #region Awakening

        public void Awakening(ulong UniqueID, AwakeType type)
        {
            if (NPCPage == null || !String.Equals(NPCPage.Key, NPCScript.AwakeningKey, StringComparison.CurrentCultureIgnoreCase))
                return;

            if (type == AwakeType.None) return;

            for (int i = 0; i < Info.Inventory.Length; i++)
            {
                UserItem item = Info.Inventory[i];
                if (item == null || item.UniqueID != UniqueID) continue;

                Awake awake = item.Awake;

                if (item.Info.Bind.HasFlag(BindMode.DontUpgrade))
                {
                    Enqueue(new S.Awakening { result = -1, removeID = -1 });
                    return;
                }

                if (item.RentalInformation != null && item.RentalInformation.BindingFlags.HasFlag(BindMode.DontUpgrade))
                {
                    Enqueue(new S.Awakening { result = -1, removeID = -1 });
                    return;
                }

                if (!item.Info.CanAwakening)
                {
                    Enqueue(new S.Awakening { result = -1, removeID = -1 });
                    return;
                }

                if (awake.IsMaxLevel())
                {
                    Enqueue(new S.Awakening { result = -2, removeID = -1 });
                    return;
                }

                if (Info.AccountInfo.Gold < item.AwakeningPrice())
                {
                    Enqueue(new S.Awakening { result = -3, removeID = -1 });
                    return;
                }

                if (HasAwakeningNeedMaterials(item, type))
                {
                    Info.AccountInfo.Gold -= item.AwakeningPrice();
                    Enqueue(new S.LoseGold { Gold = item.AwakeningPrice() });

                    bool[] isHit;

                    switch (awake.UpgradeAwake(item, type, out isHit))
                    {
                        case -1:
                            Enqueue(new S.Awakening { result = -1, removeID = -1 });
                            break;
                        case 0:
                            AwakeningEffect(false, isHit);
                            Info.Inventory[i] = null;
                            Enqueue(new S.Awakening { result = 0, removeID = (long)item.UniqueID });
                            break;
                        case 1:
                            Enqueue(new S.RefreshItem { Item = item });
                            AwakeningEffect(true, isHit);
                            Enqueue(new S.Awakening { result = 1, removeID = -1 });
                            break;
                        default:
                            break;
                    }
                }
            }
        }

        public void DowngradeAwakening(ulong UniqueID)
        {
            if (NPCPage == null || !String.Equals(NPCPage.Key, NPCScript.DowngradeKey, StringComparison.CurrentCultureIgnoreCase))
                return;

            for (int i = 0; i < Info.Inventory.Length; i++)
            {
                UserItem item = Info.Inventory[i];
                if (item != null)
                {
                    if (item.UniqueID == UniqueID)
                    {
                        if (item.RentalInformation != null)
                        {
                            ReceiveChat($"无法降级处理 {item.FriendlyName} 因为物品附属于 {item.RentalInformation.OwnerName}", ChatType.System);
                            return;
                        }

                        if (Info.AccountInfo.Gold >= item.DowngradePrice())
                        {
                            Info.AccountInfo.Gold -= item.DowngradePrice();
                            Enqueue(new S.LoseGold { Gold = item.DowngradePrice() });

                            Awake awake = item.Awake;
                            int result = awake.RemoveAwake();
                            switch (result)
                            {
                                case 0:
                                    ReceiveChat(string.Format("{0} : 觉醒降级失败 0", item.FriendlyName), ChatType.System);
                                    break;
                                case 1:
                                    ushort maxDura = (Envir.Random.Next(20) == 0) ? (ushort)(item.MaxDura - 1000) : item.MaxDura;
                                    if (maxDura < 1000) maxDura = 1000;

                                    Info.Inventory[i].CurrentDura = (Info.Inventory[i].CurrentDura >= maxDura) ? maxDura : Info.Inventory[i].CurrentDura;
                                    Info.Inventory[i].MaxDura = maxDura;
                                    ReceiveChat(string.Format("{0} : 降级成功. 觉醒等级至 {1}", item.FriendlyName, item.Awake.GetAwakeLevel()), ChatType.System);
                                    Enqueue(new S.RefreshItem { Item = item });
                                    break;
                                default:
                                    break;
                            }
                        }
                    }
                }
            }
        }

        public void DisassembleItem(ulong UniqueID)
        {
            if (NPCPage == null || !String.Equals(NPCPage.Key, NPCScript.DisassembleKey, StringComparison.CurrentCultureIgnoreCase))
                return;

            for (int i = 0; i < Info.Inventory.Length; i++)
            {
                UserItem item = Info.Inventory[i];

                if (item == null || item.UniqueID != UniqueID)
                    continue;

                if (item.Info.Bind.HasFlag(BindMode.UnableToDisassemble))
                {
                    ReceiveChat($"无法完成拆解 {item.FriendlyName}", ChatType.System);
                    return;
                }

                if (item.RentalInformation != null && item.RentalInformation.BindingFlags.HasFlag(BindMode.UnableToDisassemble))
                {
                    ReceiveChat($"无法完成拆解 {item.FriendlyName} 因为物品附属于 {item.RentalInformation.OwnerName}", ChatType.System);
                    return;
                }

                if (Info.AccountInfo.Gold >= item.DisassemblePrice())
                {
                    List<ItemInfo> dropList = new List<ItemInfo>();
                    foreach (DropInfo drop in Envir.AwakeningDrops)
                    {
                        if (drop.Item.Grade == item.Info.Grade - 1 ||
                            drop.Item.Grade == item.Info.Grade + 1)
                        {
                            if (Envir.Random.Next((drop.Chance <= 0) ? 1 : drop.Chance) == 0)
                            {
                                dropList.Add(drop.Item);
                            }
                        }

                        if (drop.Item.Grade == item.Info.Grade)
                        {
                            dropList.Add(drop.Item);
                        }
                    }

                    if (dropList.Count == 0) continue;

                    UserItem gainItem = Envir.CreateDropItem(dropList[Envir.Random.Next(dropList.Count)]);
                    if (gainItem == null) continue;
                    gainItem.Count = (ushort)Envir.Random.Next(Math.Min(ushort.MaxValue, (int)((((byte)item.Info.Grade * item.Info.RequiredAmount) / 10) + item.Quality())));
                    if (gainItem.Count < 1) gainItem.Count = 1;

                    GainItem(gainItem);

                    Enqueue(new S.LoseGold { Gold = item.DisassemblePrice() });
                    Info.AccountInfo.Gold -= item.DisassemblePrice();

                    Enqueue(new S.DeleteItem { UniqueID = item.UniqueID, Count = item.Count });
                    Info.Inventory[i] = null;
                }
            }
        }

        public void ResetAddedItem(ulong UniqueID)
        {
            if (NPCPage == null || !String.Equals(NPCPage.Key, NPCScript.ResetKey, StringComparison.CurrentCultureIgnoreCase))
                return;

            for (int i = 0; i < Info.Inventory.Length; i++)
            {
                UserItem item = Info.Inventory[i];
                if (item != null)
                {
                    if (item.UniqueID == UniqueID)
                    {
                        if (item.RentalInformation != null)
                        {
                            ReceiveChat($"无法重置 {item.FriendlyName} 因为它属于 {item.RentalInformation.OwnerName}", ChatType.System);
                            return;
                        }

                        if (Info.AccountInfo.Gold >= item.ResetPrice())
                        {
                            Info.AccountInfo.Gold -= item.ResetPrice();
                            Enqueue(new S.LoseGold { Gold = item.ResetPrice() });

                            UserItem newItem = new UserItem(item.Info);

                            ushort maxDura = (Envir.Random.Next(20) == 0) ? (ushort)(item.MaxDura - 1000) : item.MaxDura;
                            if (maxDura < 1000) maxDura = 1000;

                            newItem.UniqueID = item.UniqueID;
                            newItem.ItemIndex = item.ItemIndex;
                            newItem.CurrentDura = (item.CurrentDura >= maxDura) ? maxDura : item.CurrentDura;
                            newItem.MaxDura = maxDura;
                            newItem.Count = item.Count;
                            newItem.Slots = item.Slots;
                            newItem.Awake = item.Awake;
                            newItem.ExpireInfo = item.ExpireInfo;
                            newItem.SealedInfo = item.SealedInfo;

                            Info.Inventory[i] = newItem;

                            Enqueue(new S.RefreshItem { Item = Info.Inventory[i] });
                        }
                    }
                }
            }
        }

        public void AwakeningNeedMaterials(ulong UniqueID, AwakeType type)
        {
            if (type == AwakeType.None) return;

            foreach (UserItem item in Info.Inventory)
            {
                if (item != null)
                {
                    if (item.UniqueID == UniqueID)
                    {
                        Awake awake = item.Awake;

                        byte[] materialCount = new byte[2];
                        int idx = 0;
                        foreach (List<byte> material in Awake.AwakeMaterials[(int)type - 1])
                        {
                            byte materialRate = (byte)(Awake.AwakeMaterialRate[(int)item.Info.Grade - 1] * (float)awake.GetAwakeLevel());
                            materialCount[idx] = material[(int)item.Info.Grade - 1];
                            materialCount[idx] += materialRate;
                            idx++;
                        }

                        ItemInfo[] materials = new ItemInfo[2];

                        foreach (ItemInfo info in Envir.ItemInfoList)
                        {
                            if (item.Info.Grade == info.Grade &&
                                info.Type == ItemType.觉醒物品)
                            {
                                if (info.Shape == (short)type - 1)
                                {
                                    materials[0] = info;
                                }
                                else if (info.Shape == 100)
                                {
                                    materials[1] = info;
                                }
                            }
                        }

                        Enqueue(new S.AwakeningNeedMaterials { Materials = materials, MaterialsCount = materialCount });
                        break;
                    }
                }
            }
        }

        public void AwakeningEffect(bool isSuccess, bool[] isHit)
        {
            for (int i = 0; i < 5; i++)
            {
                Enqueue(new S.ObjectEffect { ObjectID = ObjectID, Effect = isHit[i] ? SpellEffect.AwakeningHit : SpellEffect.AwakeningMiss, EffectType = 0, DelayTime = (uint)(i * 500) });
                Broadcast(new S.ObjectEffect { ObjectID = ObjectID, Effect = isHit[i] ? SpellEffect.AwakeningHit : SpellEffect.AwakeningMiss, EffectType = 0, DelayTime = (uint)(i * 500) });
            }

            Enqueue(new S.ObjectEffect { ObjectID = ObjectID, Effect = isSuccess ? SpellEffect.AwakeningSuccess : SpellEffect.AwakeningFail, EffectType = 0, DelayTime = 2500 });
            Broadcast(new S.ObjectEffect { ObjectID = ObjectID, Effect = isSuccess ? SpellEffect.AwakeningSuccess : SpellEffect.AwakeningFail, EffectType = 0, DelayTime = 2500 });
        }

        public bool HasAwakeningNeedMaterials(UserItem item, AwakeType type)
        {
            Awake awake = item.Awake;

            byte[] materialCount = new byte[2];

            int idx = 0;
            foreach (List<byte> material in Awake.AwakeMaterials[(int)type - 1])
            {
                byte materialRate = (byte)(Awake.AwakeMaterialRate[(int)item.Info.Grade - 1] * (float)awake.GetAwakeLevel());
                materialCount[idx] = material[(int)item.Info.Grade - 1];
                materialCount[idx] += materialRate;
                idx++;
            }

            byte[] currentCount = new byte[2] { 0, 0 };

            for (int i = 0; i < Info.Inventory.Length; i++)
            {
                UserItem materialItem = Info.Inventory[i];
                if (materialItem != null)
                {
                    if (materialItem.Info.Grade == item.Info.Grade &&
                        materialItem.Info.Type == ItemType.觉醒物品)
                    {
                        if (materialItem.Info.Shape == ((int)type - 1) &&
                            materialCount[0] - currentCount[0] != 0)
                        {
                            if (materialItem.Count <= materialCount[0] - currentCount[0])
                            {
                                currentCount[0] += (byte)materialItem.Count;
                            }
                            else if (materialItem.Count > materialCount[0] - currentCount[0])
                            {
                                currentCount[0] = (byte)(materialCount[0] - currentCount[0]);
                            }
                        }
                        else if (materialItem.Info.Shape == 100 &&
                            materialCount[1] - currentCount[1] != 0)
                        {
                            if (materialItem.Count <= materialCount[1] - currentCount[1])
                            {
                                currentCount[1] += (byte)materialItem.Count;
                            }
                            else if (materialItem.Count > materialCount[1] - currentCount[1])
                            {
                                currentCount[1] = (byte)(materialCount[1] - currentCount[1]);
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < materialCount.Length; i++)
            {
                if (materialCount[i] != currentCount[i])
                {
                    Enqueue(new S.Awakening { result = -4, removeID = -1 });
                    return false;
                }
            }

            for (int i = 0; i < Info.Inventory.Length; i++)
            {
                if (Info.Inventory[i] != null)
                {
                    if (Info.Inventory[i].Info.Grade == item.Info.Grade &&
                        Info.Inventory[i].Info.Type == ItemType.觉醒物品)
                    {
                        if (Info.Inventory[i].Info.Shape == ((int)type - 1) &&
                            currentCount[0] > 0)
                        {
                            if (Info.Inventory[i].Count <= currentCount[0])
                            {
                                Enqueue(new S.DeleteItem { UniqueID = Info.Inventory[i].UniqueID, Count = Info.Inventory[i].Count });
                                currentCount[0] -= (byte)Info.Inventory[i].Count;
                                Info.Inventory[i] = null;
                            }
                            else if (Info.Inventory[i].Count > currentCount[0])
                            {
                                Enqueue(new S.DeleteItem { UniqueID = Info.Inventory[i].UniqueID, Count = currentCount[0] });
                                Info.Inventory[i].Count -= currentCount[0];
                                currentCount[0] = 0;
                            }
                        }
                        else if (Info.Inventory[i].Info.Shape == 100 &&
                            currentCount[1] > 0)
                        {
                            if (Info.Inventory[i].Count <= currentCount[1])
                            {
                                Enqueue(new S.DeleteItem { UniqueID = Info.Inventory[i].UniqueID, Count = Info.Inventory[i].Count });
                                currentCount[1] -= (byte)Info.Inventory[i].Count;
                                Info.Inventory[i] = null;
                            }
                            else if (Info.Inventory[i].Count > currentCount[1])
                            {
                                Enqueue(new S.DeleteItem { UniqueID = Info.Inventory[i].UniqueID, Count = currentCount[1] });
                                Info.Inventory[i].Count -= currentCount[1];
                                currentCount[1] = 0;
                            }
                        }
                    }
                }
            }
            return true;
        }

        #endregion

        #region Groups

        public void SwitchGroup(bool allow)
        {
            Enqueue(new S.SwitchGroup { AllowGroup = allow });

            if (AllowGroup == allow) return;
            AllowGroup = allow;

            if (AllowGroup || GroupMembers == null) return;

            LeaveGroup();
        }

        public void LeaveGroup()
        {
            if (GroupMembers != null)
            {
                GroupMembers.Remove(this);

                if (GroupMembers.Count > 1)
                {
                    Packet p = new S.DeleteMember { Name = Name };

                    for (int i = 0; i < GroupMembers.Count; i++)
                    {
                        GroupMembers[i].Enqueue(p);
                    }
                }
                else
                {
                    GroupMembers[0].Enqueue(new S.DeleteGroup());
                    GroupMembers[0].GroupMembers = null;
                }

                GroupMembers = null;
            }
        }

        public void AddMember(string name)
        {
            if (Envir.Time < NextGroupInviteTime) return;
            NextGroupInviteTime = Envir.Time + Settings.GroupInviteDelay;
            if (GroupMembers != null && GroupMembers[0] != this)
            {
                ReceiveChat("你不是组长", ChatType.System);
                return;
            }

            if (GroupMembers != null && GroupMembers.Count >= Globals.MaxGroup)
            {
                ReceiveChat("组已达到最大成员数", ChatType.System);
                return;
            }

            PlayerObject player = Envir.GetPlayer(name);

            if (player == null)
            {
                ReceiveChat(name + " 未在线", ChatType.System);
                return;
            }
            if (player == this)
            {
                ReceiveChat("组队不能添加自己", ChatType.System);
                return;
            }

            if (!player.AllowGroup)
            {
                ReceiveChat(name + " 未开启组队", ChatType.System);
                return;
            }

            if (player.GroupMembers != null)
            {
                ReceiveChat(name + " 已在另一个组中", ChatType.System);
                return;
            }

            if (player.GroupInvitation != null)
            {
                ReceiveChat(name + " 已收到其他玩家邀请", ChatType.System);
                return;
            }

            SwitchGroup(true);
            player.Enqueue(new S.GroupInvite { Name = Name });
            player.GroupInvitation = this;

        }
        public void DelMember(string name)
        {
            if (GroupMembers == null)
            {
                ReceiveChat("不在一个小组内", ChatType.System);
                return;
            }
            if (GroupMembers[0] != this)
            {
                ReceiveChat("你不是组长", ChatType.System);
                return;
            }

            PlayerObject player = null;

            for (int i = 0; i < GroupMembers.Count; i++)
            {
                if (String.Compare(GroupMembers[i].Name, name, StringComparison.OrdinalIgnoreCase) != 0) continue;
                player = GroupMembers[i];
                break;
            }

            if (player == null)
            {
                ReceiveChat(name + " 不在本组内", ChatType.System);
                return;
            }

            player.Enqueue(new S.DeleteGroup());
            player.LeaveGroup();
        }

        public void GroupInvite(bool accept)
        {
            if (GroupInvitation == null)
            {
                ReceiveChat("尚未被邀请加入某个组", ChatType.System);
                return;
            }

            if (!accept)
            {
                GroupInvitation.ReceiveChat(Name + " 已拒绝邀请", ChatType.System);
                GroupInvitation = null;
                return;
            }

            if (GroupMembers != null)
            {
                ReceiveChat(string.Format("不能加入 {0} 的组", GroupInvitation.Name), ChatType.System);
                GroupInvitation = null;
                return;
            }

            if (GroupInvitation.GroupMembers != null && GroupInvitation.GroupMembers[0] != GroupInvitation)
            {
                ReceiveChat(GroupInvitation.Name + " 不再是组长", ChatType.System);
                GroupInvitation = null;
                return;
            }

            if (GroupInvitation.GroupMembers != null && GroupInvitation.GroupMembers.Count >= Globals.MaxGroup)
            {
                ReceiveChat(GroupInvitation.Name + "的组的成员数已满", ChatType.System);
                GroupInvitation = null;
                return;
            }
            if (!GroupInvitation.AllowGroup)
            {
                ReceiveChat(GroupInvitation.Name + " 不在允许组中", ChatType.System);
                GroupInvitation = null;
                return;
            }
            if (GroupInvitation.Node == null)
            {
                ReceiveChat(GroupInvitation.Name + " 未在线", ChatType.System);
                GroupInvitation = null;
                return;
            }

            if (GroupInvitation.GroupMembers == null)
            {
                GroupInvitation.GroupMembers = new List<PlayerObject> { GroupInvitation };
                GroupInvitation.Enqueue(new S.AddMember { Name = GroupInvitation.Name });
                GroupInvitation.Enqueue(new S.GroupMembersMap { PlayerName = GroupInvitation.Name, PlayerMap = GroupInvitation.CurrentMap.Info.Title });
                GroupInvitation.Enqueue(new S.SendMemberLocation { MemberName = GroupInvitation.Name, MemberLocation = GroupInvitation.CurrentLocation });
            }

            Packet p = new S.AddMember { Name = Name };
            GroupMembers = GroupInvitation.GroupMembers;
            GroupInvitation = null;

            for (int i = 0; i < GroupMembers.Count; i++)
            {
                PlayerObject member = GroupMembers[i];

                member.Enqueue(p);
                Enqueue(new S.AddMember { Name = member.Name });

                if (CurrentMap != member.CurrentMap || !Functions.InRange(CurrentLocation, member.CurrentLocation, Globals.DataRange)) continue;

                byte time = Math.Min(byte.MaxValue, (byte)Math.Max(5, (RevTime - Envir.Time) / 1000));

                member.Enqueue(new S.ObjectHealth { ObjectID = ObjectID, Percent = PercentHealth, Expire = time });
                Enqueue(new S.ObjectHealth { ObjectID = member.ObjectID, Percent = member.PercentHealth, Expire = time });

                if (Hero != null)
                {
                    member.Enqueue(new S.ObjectHealth { ObjectID = Hero.ObjectID, Percent = Hero.PercentHealth, Expire = time }); // Send Party Leader's HeroHP to Group Members
                }
                if (member.Hero != null)
                {
                    Enqueue(new S.ObjectHealth { ObjectID = member.Hero.ObjectID, Percent = member.Hero.PercentHealth, Expire = time }); // Send Party Members HeroHP to Leader
                }

                for (int j = 0; j < member.Pets.Count; j++)
                {
                    MonsterObject pet = member.Pets[j];

                    Enqueue(new S.ObjectHealth { ObjectID = pet.ObjectID, Percent = pet.PercentHealth, Expire = time }); 
                }
            }

            GroupMembers.Add(this);

            for (int j = 0; j < Pets.Count; j++)
            {
                Pets[j].BroadcastHealthChange();
            }

            Enqueue(p);
            GroupMemberMapNameChanged();
            GetPlayerLocation();
        }
        public void GroupMemberMapNameChanged()
        {
            if (GroupMembers == null) return;

            for (int i = 0; i < GroupMembers.Count; i++)
            {
                PlayerObject member = GroupMembers[i];
                member.Enqueue(new S.GroupMembersMap { PlayerName = Name, PlayerMap = CurrentMap.Info.Title });
                Enqueue(new S.GroupMembersMap { PlayerName = member.Name, PlayerMap = member.CurrentMap.Info.Title });
            }
            Enqueue(new S.GroupMembersMap { PlayerName = Name, PlayerMap = CurrentMap.Info.Title });
        }

        #endregion

        #region Heroes
        public void NewHero(C.NewHero p)
        {
            if (!Envir.CanCreateHero(p, Connection, IsGM))
                return;

            int heroCount = Info.Heroes.Count(x => x != null);
            if (heroCount >= Info.MaximumHeroCount)
            {
                Enqueue(new S.NewHero { Result = 4 });
                return;
            }

            bool passedItemCheck = true;
            ItemInfo itemInfo = Envir.GetItemInfo(Settings.HeroSealItemName);
            if (itemInfo != null && FreeSpace(Info.Inventory) == 0)
                passedItemCheck = false;

            if (!passedItemCheck)
            {
                Enqueue(new S.NewHero { Result = 6 });
                return;
            }

            var info = new HeroInfo(p) { Index = ++Envir.NextHeroID };            
            Envir.HeroList.Add(info);

            if (itemInfo != null)
            {
                UserItem item = Envir.CreateFreshItem(itemInfo);
                item.AddedStats[Stat.Hero] = info.Index;
                GainItem(item);
            }
            else
                AddHero(info);

            Enqueue(new S.NewHero { Result = 10 });            
        }

        public HeroObject GetHero()
        {
            if (HasHero && HeroSpawned)
                return Hero;

            return null;
        }

        public void SetAutoPotValue(Stat stat, uint value)
        {
            if (!HeroSpawned || !Hero.AutoPot) return;

            if (stat == Stat.HP)
                Hero.AutoHPPercent = (byte)Math.Min(99, value);
            else
                Hero.AutoMPPercent = (byte)Math.Min(99, value);

            Enqueue(new S.SetAutoPotValue() { Stat = stat, Value = value });
        }

        public void SetAutoPotItem(MirGridType Grid, int ItemIndex)
        {
            if (!HeroSpawned || !Hero.AutoPot) return;

            if (Envir.GetItemInfo(ItemIndex) == null)
                ItemIndex = 0;

            if (Grid == MirGridType.HeroHPItem)
                Hero.HPItemIndex = ItemIndex;
            else
                Hero.MPItemIndex = ItemIndex;
            Enqueue(new S.SetAutoPotItem() { Grid = Grid, ItemIndex = ItemIndex });
        }

        public void SetHeroBehaviour(HeroBehaviour behaviour)
        {
            if (!HeroSpawned) return;
            if (Info.HeroBehaviour == behaviour) return;

            Info.HeroBehaviour = behaviour;
            Enqueue(new S.SetHeroBehaviour() { Behaviour = behaviour });

            Hero.Target = null;
            Hero.SearchTime = 0;
        }

        public void ChangeHero(int index)
        {
            if (Info.Heroes.Length <= index) return;
            bool respawn = Info.HeroSpawned;

            if (Hero != null)
            {
                DespawnHero();
                Info.HeroSpawned = false;
                Enqueue(new S.UpdateHeroSpawnState { State = HeroSpawnState.None });
            }

            HeroInfo temp = Info.Heroes[index];
            Info.Heroes[index] = Info.Heroes[0];
            Info.Heroes[0] = temp;
            CurrentHero = Info.Heroes[0];

            Enqueue(new S.ChangeHero() { FromIndex = index - 1 });

            if (Info.Heroes[0] == null || !respawn)
            {
                Enqueue(new S.UpdateHeroSpawnState { State = HeroSpawnState.Unsummoned });
                return;
            }

            SummonHero();
        }
        #endregion

        #region Guilds

        public bool CreateGuild(string guildName)
        {
            if ((MyGuild != null) || (Info.GuildIndex != -1)) return false;
            if (Envir.GetGuild(guildName) != null) return false;

            if (Info.Level < Settings.Guild_RequiredLevel)
            {
                ReceiveChat(String.Format("创建公会需要等级：{0}", Settings.Guild_RequiredLevel), ChatType.System);
                return false;
            }

            if(!Info.AccountInfo.AdminAccount && String.Equals(guildName, Settings.NewbieGuild, StringComparison.OrdinalIgnoreCase))
            {
                ReceiveChat($"不能使用：新人公会 这个名字", ChatType.System);
                return false;
            }

            if (!Info.AccountInfo.AdminAccount)
            {
                //check if we have the required items
                for (int i = 0; i < Settings.Guild_CreationCostList.Count; i++)
                {
                    GuildItemVolume Required = Settings.Guild_CreationCostList[i];
                    if (Required.Item == null)
                    {
                        if (Info.AccountInfo.Gold < Required.Amount)
                        {
                            ReceiveChat(String.Format("创建公会需要：{0} 金币", Required.Amount), ChatType.System);
                            return false;
                        }
                    }
                    else
                    {
                        ushort count = (ushort)Math.Min(Required.Amount, ushort.MaxValue);

                        foreach (var item in Info.Inventory.Where(item => item != null && item.Info == Required.Item))
                        {
                            if ((Required.Item.Type == ItemType.矿石) && (item.CurrentDura / 1000 > Required.Amount))
                            {
                                count = 0;
                                break;
                            }
                            if (item.Count > count)
                                count = 0;
                            else
                                count = (ushort)(count - item.Count);
                            if (count == 0) break;
                        }
                        if (count != 0)
                        {
                            if (Required.Amount == 1)
                                ReceiveChat(String.Format("{0} 需要创建公会", Required.Item.FriendlyName), ChatType.System);
                            else
                            {
                                if (Required.Item.Type == ItemType.矿石)
                                    ReceiveChat(string.Format("{0} 具有纯度 {1} 被重新招募来创建一个公会", Required.Item.FriendlyName, Required.Amount / 1000), ChatType.System);
                                else
                                    ReceiveChat(string.Format("Insufficient {0}, 你需要 {1} 创建一个公会", Required.Item.FriendlyName, Required.Amount), ChatType.System);
                            }
                            return false;
                        }
                    }
                }

                //take the required items
                for (int i = 0; i < Settings.Guild_CreationCostList.Count; i++)
                {
                    GuildItemVolume Required = Settings.Guild_CreationCostList[i];
                    if (Required.Item == null)
                    {
                        if (Info.AccountInfo.Gold >= Required.Amount)
                        {
                            Info.AccountInfo.Gold -= Required.Amount;
                            Enqueue(new S.LoseGold { Gold = Required.Amount });
                        }
                    }
                    else
                    {
                        ushort count = (ushort)Math.Min(Required.Amount, ushort.MaxValue);

                        for (int o = 0; o < Info.Inventory.Length; o++)
                        {
                            UserItem item = Info.Inventory[o];
                            if (item == null) continue;
                            if (item.Info != Required.Item) continue;

                            if ((Required.Item.Type == ItemType.矿石) && (item.CurrentDura / 1000 > Required.Amount))
                            {
                                Enqueue(new S.DeleteItem { UniqueID = item.UniqueID, Count = item.Count });
                                Info.Inventory[o] = null;
                                break;
                            }
                            if (count > item.Count)
                            {
                                Enqueue(new S.DeleteItem { UniqueID = item.UniqueID, Count = item.Count });
                                Info.Inventory[o] = null;
                                count -= item.Count;
                                continue;
                            }

                            Enqueue(new S.DeleteItem { UniqueID = item.UniqueID, Count = (ushort)count });
                            if (count == item.Count)
                                Info.Inventory[o] = null;
                            else
                                item.Count -= (ushort)count;
                            break;
                        }
                    }
                }
                RefreshStats();
            }
            
            //make the guild
            var guildInfo = new GuildInfo(this, guildName) { GuildIndex = ++Envir.NextGuildID };
            Envir.GuildList.Add(guildInfo);

            GuildObject guild = new GuildObject(guildInfo);
            Info.GuildIndex = guildInfo.GuildIndex;

            MyGuild = guild;
            MyGuildRank = guild.FindRank(Name);
            GuildMembersChanged = true;
            GuildNoticeChanged = true;
            GuildCanRequestItems = true;

            //tell us we now have a guild
            BroadcastInfo();
            MyGuild.SendGuildStatus(this);

            return true;
        }

        public void EditGuildMember(string Name, string RankName, byte RankIndex, byte ChangeType)
        {
            if ((MyGuild == null) || (MyGuildRank == null))
            {
                ReceiveChat(GameLanguage.NotInGuild, ChatType.System);
                return;
            }
            switch (ChangeType)
            {
                case 0: //add member
                    if (!MyGuildRank.Options.HasFlag(GuildRankOptions.CanRecruit))
                    {
                        ReceiveChat("你不能招募新会员", ChatType.System);
                        return;
                    }

                    if (Name == "") return;

                    PlayerObject player = Envir.GetPlayer(Name);
                    if (player == null)
                    {
                        ReceiveChat(String.Format("{0} 不在线", Name), ChatType.System);
                        return;
                    }
                    if ((player.MyGuild != null) || (player.MyGuildRank != null) || (player.Info.GuildIndex != -1))
                    {
                        ReceiveChat(String.Format("{0} 已经加入行会了", Name), ChatType.System);
                        return;
                    }
                    if (!player.EnableGuildInvite)
                    {
                        ReceiveChat(String.Format("{0} 未开启行会邀请", Name), ChatType.System);
                        return;
                    }
                    if (player.PendingGuildInvite != null)
                    {
                        ReceiveChat(string.Format("{0} 已有一个行会邀请待定", Name), ChatType.System);
                        return;
                    }

                    if (MyGuild.IsAtWar())
                    {
                        ReceiveChat("交战状态中不能操作", ChatType.System);
                        return;
                    }

                    player.Enqueue(new S.GuildInvite { Name = MyGuild.Name });
                    player.PendingGuildInvite = MyGuild;
                    break;
                case 1: //delete member
                    if (!MyGuildRank.Options.HasFlag(GuildRankOptions.CanKick))
                    {
                        ReceiveChat("不允许删除会员", ChatType.System);
                        return;
                    }
                    if (Name == "") return;

                    if (!MyGuild.DeleteMember(this, Name))
                    {
                        return;
                    }
                    break;
                case 2: //promote member (and it'll auto create a new rank at bottom if the index > total ranks!)
                    if (!MyGuildRank.Options.HasFlag(GuildRankOptions.CanChangeRank))
                    {
                        ReceiveChat("无权限改变", ChatType.System);
                        return;
                    }
                    if (Name == "") return;
                    MyGuild.ChangeRank(this, Name, RankIndex, RankName);
                    break;
                case 3: //change rank name
                    if (!MyGuildRank.Options.HasFlag(GuildRankOptions.CanChangeRank))
                    {
                        ReceiveChat("无权限改变", ChatType.System);
                        return;
                    }
                    if ((RankName == "") || (RankName.Length < 3))
                    {
                        ReceiveChat("重命名需要3个以上字符", ChatType.System);
                        return;
                    }
                    if (RankName.Contains("\\") || RankName.Length > 20)
                    {
                        return;
                    }
                    if (!MyGuild.ChangeRankName(this, RankName, RankIndex))
                        return;
                    break;
                case 4: //new rank
                    if (!MyGuildRank.Options.HasFlag(GuildRankOptions.CanChangeRank))
                    {
                        ReceiveChat("无权限改变", ChatType.System);
                        return;
                    }
                    if (MyGuild.Ranks.Count > 254)
                    {
                        ReceiveChat("没有更多的行会官阶可用", ChatType.System);
                        return;
                    }
                    MyGuild.NewRank(this);
                    break;
                case 5: //change rank setting
                    if (!MyGuildRank.Options.HasFlag(GuildRankOptions.CanChangeRank))
                    {
                        ReceiveChat("无权限改变", ChatType.System);
                        return;
                    }
                    int temp;

                    if (!int.TryParse(RankName, out temp))
                    {
                        return;
                    }
                    MyGuild.ChangeRankOption(this, RankIndex, temp, Name);
                    break;
            }
        }
        public void EditGuildNotice(List<string> notice)
        {
            if ((MyGuild == null) || (MyGuildRank == null))
            {
                ReceiveChat(GameLanguage.NotInGuild, ChatType.System);
                return;
            }
            if (!MyGuildRank.Options.HasFlag(GuildRankOptions.CanChangeNotice))
            {

                ReceiveChat("无权限改变行会公告", ChatType.System);
                return;
            }
            if (notice.Count > 200)
            {
                ReceiveChat("行会公告不能超过200行", ChatType.System);
                return;
            }
            MyGuild.NewNotice(notice);
        }
        public void GuildInvite(bool accept)
        {
            if (PendingGuildInvite == null)
            {
                ReceiveChat("没有被邀请加入行会", ChatType.System);
                return;
            }
            if (!accept)
            {
                PendingGuildInvite = null;
                return;
            }
            if (!PendingGuildInvite.HasRoom())
            {
                ReceiveChat(String.Format("行会: {0} 已满员", PendingGuildInvite.Name), ChatType.System);
                return;
            }
            PendingGuildInvite.NewMember(this);
            Info.GuildIndex = PendingGuildInvite.Guildindex;
            MyGuild = PendingGuildInvite;
            MyGuildRank = PendingGuildInvite.FindRank(Name);
            GuildMembersChanged = true;
            GuildNoticeChanged = true;
            //tell us we now have a guild
            BroadcastInfo();
            MyGuild.SendGuildStatus(this);
            PendingGuildInvite = null;
            EnableGuildInvite = false;
            GuildCanRequestItems = true;
            //refresh guildbuffs
            RefreshStats();
            if (MyGuild.BuffList.Count > 0)
                Enqueue(new S.GuildBuffList() { ActiveBuffs = MyGuild.BuffList});
        }
        public void RequestGuildInfo(byte Type)
        {
            if (MyGuild == null) return;
            if (MyGuildRank == null) return;
            switch (Type)
            {
                case 0://notice
                    if (GuildNoticeChanged)
                        Enqueue(new S.GuildNoticeChange() { notice = MyGuild.Info.Notice });
                    GuildNoticeChanged = false;
                    break;
                case 1://memberlist
                    if (GuildMembersChanged)
                        Enqueue(new S.GuildMemberChange() { Status = 255, Ranks = MyGuild.Ranks });
                    break;
            }
        }
        public void GuildNameReturn(string Name)
        {
            if (Name == "") CanCreateGuild = false;
            if (!CanCreateGuild) return;
            if ((Name.Length < 3) || (Name.Length > 20))
            {
                ReceiveChat("行会名称不能少于3个字符", ChatType.System);
                CanCreateGuild = false;
                return;
            }
            if (Name.Contains('\\'))
            {
                CanCreateGuild = false;
                return;
            }
            if (MyGuild != null)
            {
                ReceiveChat("已经是行会成员", ChatType.System);
                CanCreateGuild = false;
                return;
            }
            GuildObject guild = Envir.GetGuild(Name);
            if (guild != null)
            {
                ReceiveChat(string.Format("行会 {0} 已经存在", Name), ChatType.System);
                CanCreateGuild = false;
                return;
            }

            CreateGuild(Name);
            CanCreateGuild = false;
        }
        public void GuildStorageGoldChange(byte type, uint amount)
        {
            if ((MyGuild == null) || (MyGuildRank == null))
            {
                ReceiveChat("你不是行会成员", ChatType.System);
                return;
            }

            if (!InSafeZone)
            {
                ReceiveChat("不能在安全区外使用行会仓库", ChatType.System);
                return;
            }

            if (type == 0)//donate
            {
                if (Account.Gold < amount)
                {
                    ReceiveChat("金币不足", ChatType.System);
                    return;
                }

                if ((MyGuild.Gold + (ulong)amount) > uint.MaxValue)
                {
                    ReceiveChat("已达行会金币上限", ChatType.System);
                    return;
                }

                Account.Gold -= amount;
                MyGuild.Gold += amount;
                Enqueue(new S.LoseGold { Gold = amount });
                MyGuild.SendServerPacket(new S.GuildStorageGoldChange() { Type = 0, Name = Info.Name, Amount = amount });
                MyGuild.NeedSave = true;
            }
            else
            {
                if (MyGuild.Gold < amount)
                {
                    ReceiveChat("金币不足", ChatType.System);
                    return;
                }

                if (!CanGainGold(amount))
                {
                    ReceiveChat("已达金币限额", ChatType.System);
                    return;
                }

                if (MyGuildRank.Index != 0)
                {
                    ReceiveChat("行会阶位不够", ChatType.System);
                    return;
                }

                MyGuild.Gold -= amount;
                GainGold(amount);
                MyGuild.SendServerPacket(new S.GuildStorageGoldChange() { Type = 1, Name = Info.Name, Amount = amount });
                MyGuild.NeedSave = true;
            }
        }
        public void GuildStorageItemChange(byte type, int from, int to)
        {
            S.GuildStorageItemChange p = new S.GuildStorageItemChange { Type = (byte)(3 + type), From = from, To = to };
            if ((MyGuild == null) || (MyGuildRank == null))
            {
                Enqueue(p);
                ReceiveChat("你不是行会成员", ChatType.System);
                return;
            }

            if (!InSafeZone && type != 3)
            {
                Enqueue(p);
                ReceiveChat("不能在安全区外使用行会仓库", ChatType.System);
                return;
            }

            switch (type)
            {
                case 0://store
                    if (!MyGuildRank.Options.HasFlag(GuildRankOptions.CanStoreItem))
                    {
                        Enqueue(p);
                        ReceiveChat("无使用仓库储物权限", ChatType.System);
                        return;
                    }
                    if (from < 0 || from >= Info.Inventory.Length)
                    {
                        Enqueue(p);
                        return;
                    }
                    if (to < 0 || to >= MyGuild.StoredItems.Length)
                    {
                        Enqueue(p);
                        return;
                    }
                    if (Info.Inventory[from] == null)
                    {
                        Enqueue(p);
                        return;
                    }
                    if (Info.Inventory[from].Info.Bind.HasFlag(BindMode.DontStore))
                    {
                        Enqueue(p);
                        return;
                    }
                    if (Info.Inventory[from].RentalInformation != null && Info.Inventory[from].RentalInformation.BindingFlags.HasFlag(BindMode.DontStore))
                    {
                        Enqueue(p);
                        return;
                    }
                    if (MyGuild.StoredItems[to] != null)
                    {
                        ReceiveChat("位置不能为空", ChatType.System);
                        Enqueue(p);
                        return;
                    }
                    MyGuild.StoredItems[to] = new GuildStorageItem() { Item = Info.Inventory[from], UserId = Info.Index };
                    Info.Inventory[from] = null;
                    RefreshBagWeight();
                    MyGuild.SendItemInfo(MyGuild.StoredItems[to].Item);
                    MyGuild.SendServerPacket(new S.GuildStorageItemChange() { Type = 0, User = Info.Index, Item = MyGuild.StoredItems[to], To = to, From = from });
                    MyGuild.NeedSave = true;
                    break;
                case 1://retrieve
                    if (!MyGuildRank.Options.HasFlag(GuildRankOptions.CanRetrieveItem))
                    {

                        ReceiveChat("没有拿取物品的权限", ChatType.System);
                        return;
                    }
                    if (from < 0 || from >= MyGuild.StoredItems.Length)
                    {
                        Enqueue(p);
                        return;
                    }
                    if (to < 0 || to >= Info.Inventory.Length)
                    {
                        Enqueue(p);
                        return;
                    }
                    if (Info.Inventory[to] != null)
                    {
                        ReceiveChat("目标位置不能为空", ChatType.System);
                        Enqueue(p);
                        return;
                    }
                    if (MyGuild.StoredItems[from] == null)
                    {
                        Enqueue(p);
                        return;
                    }
                    if (MyGuild.StoredItems[from].Item.Info.Bind.HasFlag(BindMode.DontStore))
                    {
                        Enqueue(p);
                        return;
                    }
                    Info.Inventory[to] = MyGuild.StoredItems[from].Item;
                    MyGuild.StoredItems[from] = null;
                    MyGuild.SendServerPacket(new S.GuildStorageItemChange() { Type = 1, User = Info.Index, To = to, From = from });
                    RefreshBagWeight();
                    MyGuild.NeedSave = true;
                    break;
                case 2: // Move Item
                    GuildStorageItem q = null;
                    if (!MyGuildRank.Options.HasFlag(GuildRankOptions.CanStoreItem))
                    {
                        Enqueue(p);
                        ReceiveChat("没有拿取行会仓库物品权限", ChatType.System);
                        return;
                    }
                    if (from < 0 || from >= MyGuild.StoredItems.Length)
                    {
                        Enqueue(p);
                        return;
                    }
                    if (to < 0 || to >= MyGuild.StoredItems.Length)
                    {
                        Enqueue(p);
                        return;
                    }
                    if (MyGuild.StoredItems[from] == null)
                    {
                        Enqueue(p);
                        return;
                    }
                    if (MyGuild.StoredItems[from].Item.Info.Bind.HasFlag(BindMode.DontStore))
                    {
                        Enqueue(p);
                        return;
                    }
                    if (MyGuild.StoredItems[to] != null)
                    {
                        q = MyGuild.StoredItems[to];
                    }
                    MyGuild.StoredItems[to] = MyGuild.StoredItems[from];
                    if (q != null) MyGuild.StoredItems[from] = q;
                    else MyGuild.StoredItems[from] = null;

                    MyGuild.SendItemInfo(MyGuild.StoredItems[to].Item);

                    if (MyGuild.StoredItems[from] != null) MyGuild.SendItemInfo(MyGuild.StoredItems[from].Item);

                    MyGuild.SendServerPacket(new S.GuildStorageItemChange() { Type = 2, User = Info.Index, Item = MyGuild.StoredItems[to], To = to, From = from });
                    MyGuild.NeedSave = true;
                    break;
                case 3://request list
                    if (!GuildCanRequestItems) return;
                    GuildCanRequestItems = false;
                    for (int i = 0; i < MyGuild.StoredItems.Length; i++)
                    {
                        if (MyGuild.StoredItems[i] == null) continue;
                        UserItem item = MyGuild.StoredItems[i].Item;
                        if (item == null) continue;
                        //CheckItemInfo(item.Info);
                        CheckItem(item);
                    }
                    Enqueue(new S.GuildStorageList() { Items = MyGuild.StoredItems });
                    break;
            }

        }
        public void GuildWarReturn(string Name)
        {
            if (MyGuild == null || MyGuildRank != MyGuild.Ranks[0]) return;

            GuildObject enemyGuild = Envir.GetGuild(Name);

            if (enemyGuild == null)
            {
                ReceiveChat(string.Format("未找到行会 {0}", Name), ChatType.System);
                return;
            }

            if (MyGuild == enemyGuild)
            {
                ReceiveChat("不能与自己的行会开战", ChatType.System);
                return;
            }

            if (enemyGuild.Name == Settings.NewbieGuild)
            {
                ReceiveChat("不能向新手玩家公会开战", ChatType.System);
                return;
            }

            if (MyGuild.WarringGuilds.Contains(enemyGuild))
            {
                ReceiveChat("已和该行会宣战", ChatType.System);
                return;
            }

            if (MyGuild.Gold < Settings.Guild_WarCost)
            {
                ReceiveChat("行会资金不足", ChatType.System);
                return;
            }

            if (MyGuild.GoToWar(enemyGuild))
            {
                ReceiveChat(string.Format("开始与 {0} 行会宣战", Name), ChatType.System);
                enemyGuild.SendMessage(string.Format("{0} 发动了行会战", MyGuild.Name), ChatType.System);

                MyGuild.Gold -= Settings.Guild_WarCost;
                MyGuild.SendServerPacket(new S.GuildStorageGoldChange() { Type = 2, Name = Info.Name, Amount = Settings.Guild_WarCost });
            }
        }

        public override bool AtWar(HumanObject attacker)
        {
            if (CurrentMap.Info.Fight) return true;

            if (MyGuild == null) return false;

            if (attacker is PlayerObject playerAttacker)
            {
                if (attacker == null || playerAttacker.MyGuild == null) return false;

                if (!MyGuild.WarringGuilds.Contains(playerAttacker.MyGuild)) return false;
            }

            return true;
        }
        protected override void CleanUp()
        {
            base.CleanUp();
            Account = null;            
        }

        public void GuildBuffUpdate(byte type, int id)
        {
            if (MyGuild == null) return;
            if (MyGuildRank == null) return;
            if (id < 0) return;
            switch (type)
            {
                case 0://request info list
                    if (RequestedGuildBuffInfo) return;
                    Enqueue(new S.GuildBuffList() { GuildBuffs = Settings.Guild_BuffList });
                    break;
                case 1://buy the buff
                    if (!MyGuildRank.Options.HasFlag(GuildRankOptions.CanActivateBuff))
                    {
                        ReceiveChat("公会权限不够", ChatType.System);
                        return;
                    }
                    GuildBuffInfo BuffInfo = Envir.FindGuildBuffInfo(id);
                    if (BuffInfo == null)
                    {
                        ReceiveChat("特效未被激活", ChatType.System);
                        return;
                    }
                    if (MyGuild.GetBuff(id) != null)
                    {
                        ReceiveChat("特效已激活", ChatType.System);
                        return;
                    }
                    if ((MyGuild.Info.Level < BuffInfo.LevelRequirement) || (MyGuild.Info.SparePoints < BuffInfo.PointsRequirement)) return;//client checks this so it shouldnt be possible without a moded client :p
                    MyGuild.NewBuff(id);
                    break;
                case 2://activate the buff
                    if (!MyGuildRank.Options.HasFlag(GuildRankOptions.CanActivateBuff))
                    {
                        ReceiveChat("公会权限不够", ChatType.System);
                        return;
                    }
                    GuildBuff Buff = MyGuild.GetBuff(id);
                    if (Buff == null)
                    {
                        ReceiveChat("未得到特效", ChatType.System);
                        return;
                    }
                    if ((MyGuild.Gold < Buff.Info.ActivationCost) || (Buff.Active)) return;
                    MyGuild.ActivateBuff(id);
                    break;
            }
        }

        #endregion

        #region Trading

        public void DepositTradeItem(int from, int to)
        {
            S.DepositTradeItem p = new S.DepositTradeItem { From = from, To = to, Success = false };

            if (from < 0 || from >= Info.Inventory.Length)
            {
                Enqueue(p);
                return;
            }

            if (to < 0 || to >= Info.Trade.Length)
            {
                Enqueue(p);
                return;
            }

            UserItem temp = Info.Inventory[from];

            if (temp == null)
            {
                Enqueue(p);
                return;
            }

            if (temp.Info.Bind.HasFlag(BindMode.DontTrade))
            {
                Enqueue(p);
                return;
            }

            if (temp.RentalInformation != null && temp.RentalInformation.BindingFlags.HasFlag(BindMode.DontTrade))
            {
                Enqueue(p);
                return;
            }

            if (Info.Trade[to] == null)
            {
                Info.Trade[to] = temp;
                Info.Inventory[from] = null;
                RefreshBagWeight();
                TradeItem();

                Report.ItemMoved(temp, MirGridType.Inventory, MirGridType.Trade, from, to);
                
                p.Success = true;
                Enqueue(p);
                return;
            }
            Enqueue(p);

        }
        public void RetrieveTradeItem(int from, int to)
        {
            S.RetrieveTradeItem p = new S.RetrieveTradeItem { From = from, To = to, Success = false };

            if (from < 0 || from >= Info.Trade.Length)
            {
                Enqueue(p);
                return;
            }

            if (to < 0 || to >= Info.Inventory.Length)
            {
                Enqueue(p);
                return;
            }

            UserItem temp = Info.Trade[from];

            if (temp == null)
            {
                Enqueue(p);
                return;
            }

            if (Info.Inventory[to] == null)
            {
                Info.Inventory[to] = temp;
                Info.Trade[from] = null;

                p.Success = true;
                RefreshBagWeight();
                TradeItem();

                Report.ItemMoved(temp, MirGridType.Trade, MirGridType.Inventory, from, to);
            }

            Enqueue(p);
        }

        

        public void TradeRequest()
        {
            if (Envir.Time < NextTradeTime) return;
            NextTradeTime = Envir.Time + Settings.TradeDelay;

            if (TradePartner != null)
            {
                ReceiveChat("正在交易", ChatType.System);
                return;
            }

            Point target = Functions.PointMove(CurrentLocation, Direction, 1);

            if (!CurrentMap.ValidPoint(target)) return;
            Cell cell = CurrentMap.GetCell(target);
            PlayerObject player = null;

            if (cell.Objects == null || cell.Objects.Count == 0) 
            {
                ReceiveChat(GameLanguage.FaceToTrade, ChatType.System);
                return;
            } 

            for (int i = 0; i < cell.Objects.Count; i++)
            {
                MapObject ob = cell.Objects[i];
                if (ob.Race != ObjectType.Player) continue;

                player = Envir.GetPlayer(ob.Name);
            }

            if (player == null)
            {
                ReceiveChat(GameLanguage.FaceToTrade, ChatType.System);
                return;
            }

            if (player != null)
            {
                if (!Functions.FacingEachOther(Direction, CurrentLocation, player.Direction, player.CurrentLocation))
                {
                    ReceiveChat(GameLanguage.FaceToTrade, ChatType.System);
                    return;
                }

                if (player == this)
                {
                    ReceiveChat("不能与自己交易", ChatType.System);
                    return;
                }

                if (player.Dead || Dead)
                {
                    ReceiveChat("死后不能交易", ChatType.System);
                    return;
                }

                if (player.TradeInvitation != null)
                {
                    ReceiveChat(string.Format("玩家 {0} 收到了交易邀请", player.Info.Name), ChatType.System);
                    return;
                }

                if (!player.AllowTrade)
                {
                    ReceiveChat(string.Format("玩家 {0} 未开启交易", player.Info.Name), ChatType.System);
                    return;
                }

                if (!Functions.InRange(player.CurrentLocation, CurrentLocation, Globals.DataRange) || player.CurrentMap != CurrentMap)
                {
                    ReceiveChat(string.Format("玩家 {0} 超出交易范围", player.Info.Name), ChatType.System);
                    return;
                }

                if (player.TradePartner != null)
                {
                    ReceiveChat(string.Format("玩家 {0} 开始交易", player.Info.Name), ChatType.System);
                    return;
                }

                player.TradeInvitation = this;
                player.Enqueue(new S.TradeRequest { Name = Info.Name });
            }
        }
        public void TradeReply(bool accept)
        {
            if (TradeInvitation == null || TradeInvitation.Info == null)
            {
                TradeInvitation = null;
                return;
            }

            if (!accept)
            {
                TradeInvitation.ReceiveChat(string.Format("玩家 {0} 拒绝交易", Info.Name), ChatType.System);
                TradeInvitation = null;
                return;
            }

            if (TradePartner != null)
            {
                ReceiveChat("已经在交易", ChatType.System);
                TradeInvitation = null;
                return;
            }

            if (TradeInvitation.TradePartner != null)
            {
                ReceiveChat(string.Format("玩家 {0} 开始交易", TradeInvitation.Info.Name), ChatType.System);
                TradeInvitation = null;
                return;
            }

            TradePartner = TradeInvitation;
            TradeInvitation.TradePartner = this;
            TradeInvitation = null;

            Enqueue(new S.TradeAccept { Name = TradePartner.Info.Name });
            TradePartner.Enqueue(new S.TradeAccept { Name = Info.Name });
        }
        public void TradeGold(uint amount)
        {
            TradeUnlock();

            if (TradePartner == null) return;

            if (amount < 1 || Account.Gold < amount)
            {
                return;
            }

            TradeGoldAmount += amount;
            Account.Gold -= amount;

            Enqueue(new S.LoseGold { Gold = amount });
            TradePartner.Enqueue(new S.TradeGold { Amount = TradeGoldAmount });
        }
        public void TradeItem()
        {
            TradeUnlock();

            if (TradePartner == null) return;

            for (int i = 0; i < Info.Trade.Length; i++)
            {
                UserItem u = Info.Trade[i];
                if (u == null) continue;

                //TradePartner.CheckItemInfo(u.Info);
                TradePartner.CheckItem(u);
            }

            TradePartner.Enqueue(new S.TradeItem { TradeItems = Info.Trade });
        }

        public void TradeUnlock()
        {
            TradeLocked = false;

            if (TradePartner != null)
            {
                TradePartner.TradeLocked = false;
            }
        }

        public void TradeConfirm(bool confirm)
        {
            if(!confirm)
            {
                TradeLocked = false;
                return;
            }

            if (TradePartner == null)
            {
                TradeCancel();
                return;
            }

            if (!Functions.InRange(TradePartner.CurrentLocation, CurrentLocation, Globals.DataRange) || TradePartner.CurrentMap != CurrentMap ||
                !Functions.FacingEachOther(Direction, CurrentLocation, TradePartner.Direction, TradePartner.CurrentLocation))
            {
                TradeCancel();
                return;
            }

            TradeLocked = true;

            if (TradeLocked && !TradePartner.TradeLocked)
            {
                TradePartner.ReceiveChat(string.Format("交易对象 {0} 正在等待您确认交易", Info.Name), ChatType.System);
            }

            if (!TradeLocked || !TradePartner.TradeLocked) return;

            PlayerObject[] TradePair = new PlayerObject[2] { TradePartner, this };

            bool CanTrade = true;
            UserItem u;

            //check if both people can accept the others items
            for (int p = 0; p < 2; p++)
            {
                int o = p == 0 ? 1 : 0;

                if (!TradePair[o].CanGainItems(TradePair[p].Info.Trade))
                {
                    CanTrade = false;
                    TradePair[p].ReceiveChat("交易对象不能接收所有物品", ChatType.System);
                    TradePair[p].Enqueue(new S.TradeCancel { Unlock = true });

                    TradePair[o].ReceiveChat("无法接收所有物品", ChatType.System);
                    TradePair[o].Enqueue(new S.TradeCancel { Unlock = true });

                    return;
                }

                if (!TradePair[o].CanGainGold(TradePair[p].TradeGoldAmount))
                {
                    CanTrade = false;
                    TradePair[p].ReceiveChat("交易对象不能再接收金币", ChatType.System);
                    TradePair[p].Enqueue(new S.TradeCancel { Unlock = true });

                    TradePair[o].ReceiveChat("无法再接收更多的金币", ChatType.System);
                    TradePair[o].Enqueue(new S.TradeCancel { Unlock = true });

                    return;
                }
            }

            //swap items
            if (CanTrade)
            {
                for (int p = 0; p < 2; p++)
                {
                    int o = p == 0 ? 1 : 0;

                    for (int i = 0; i < TradePair[p].Info.Trade.Length; i++)
                    {
                        u = TradePair[p].Info.Trade[i];

                        if (u == null) continue;

                        TradePair[o].GainItem(u);
                        TradePair[p].Info.Trade[i] = null;

                        Report.ItemMoved(u, MirGridType.Trade, MirGridType.Inventory, i, -99, string.Format("交易 {0} 到 {1}", TradePair[p].Name, TradePair[o].Name));
                    }

                    if (TradePair[p].TradeGoldAmount > 0)
                    {
                        Report.GoldChanged(TradePair[p].TradeGoldAmount, true, string.Format("交易 {0} 到 {1}", TradePair[p].Name, TradePair[o].Name));

                        TradePair[o].GainGold(TradePair[p].TradeGoldAmount);
                        TradePair[p].TradeGoldAmount = 0;
                    }

                    TradePair[p].ReceiveChat("交易成功", ChatType.System);
                    TradePair[p].Enqueue(new S.TradeConfirm());

                    TradePair[p].TradeLocked = false;
                    TradePair[p].TradePartner = null;
                }
            }
        }
        public void TradeCancel()
        {
            TradeUnlock();

            if (TradePartner == null)
            {
                return;
            }

            PlayerObject[] TradePair = new PlayerObject[2] { TradePartner, this };

            for (int p = 0; p < 2; p++)
            {
                if (TradePair[p] != null)
                {
                    for (int t = 0; t < TradePair[p].Info.Trade.Length; t++)
                    {
                        UserItem temp = TradePair[p].Info.Trade[t];

                        if (temp == null) continue;

                        if(FreeSpace(TradePair[p].Info.Inventory) < 1)
                        {
                            TradePair[p].GainItemMail(temp, 1);
                            Report.ItemMailed(temp, temp.Count, 1);

                            TradePair[p].Enqueue(new S.DeleteItem { UniqueID = temp.UniqueID, Count = temp.Count });
                            TradePair[p].Info.Trade[t] = null;
                            continue;
                        }

                        for (int i = 0; i < TradePair[p].Info.Inventory.Length; i++)
                        {
                            if (TradePair[p].Info.Inventory[i] != null) continue;

                            //Put item back in inventory
                            if (TradePair[p].CanGainItem(temp))
                            {
                                TradePair[p].RetrieveTradeItem(t, i);
                            }
                            else //Send item to mailbox if it can no longer be stored
                            {
                                TradePair[p].GainItemMail(temp, 1);
                                Report.ItemMailed(temp, temp.Count, 1);

                                TradePair[p].Enqueue(new S.DeleteItem { UniqueID = temp.UniqueID, Count = temp.Count });
                            }

                            TradePair[p].Info.Trade[t] = null;

                            break;
                        }
                    }

                    //Put back deposited gold
                    if (TradePair[p].TradeGoldAmount > 0)
                    {
                        Report.GoldChanged(TradePair[p].TradeGoldAmount, false);

                        TradePair[p].GainGold(TradePair[p].TradeGoldAmount);
                        TradePair[p].TradeGoldAmount = 0;
                    }

                    TradePair[p].TradeLocked = false;
                    TradePair[p].TradePartner = null;

                    TradePair[p].Enqueue(new S.TradeCancel { Unlock = false });
                }
            }
        }

        #endregion        

        #region Fishing

        public void FishingCast(bool cast, bool cancel = false)
        {
            UserItem rod = Info.Equipment[(int)EquipmentSlot.武器];

            byte flexibilityStat = 0;
            sbyte successStat = 0;
            byte nibbleMin = 0, nibbleMax = 0;
            byte failedAddSuccessMin = 0, failedAddSuccessMax = 0;
            FishingProgressMax = Settings.FishingAttempts;//30;

            if (rod == null || !rod.Info.IsFishingRod || rod.CurrentDura == 0)
            {
                Fishing = false;
                return;
            }

            Point fishingPoint = Functions.PointMove(CurrentLocation, Direction, 3);

            if (fishingPoint.X < 0 || fishingPoint.Y < 0 || CurrentMap.Width < fishingPoint.X || CurrentMap.Height < fishingPoint.Y)
            {
                Fishing = false;
                return;
            }

            Cell fishingCell = CurrentMap.Cells[fishingPoint.X, fishingPoint.Y];

            if (fishingCell.FishingAttribute < 0)
            {
                Fishing = false;
                return;
            }

            flexibilityStat = (byte)Math.Max(byte.MinValue, (Math.Min(byte.MaxValue, flexibilityStat + rod.Info.Stats[Stat.暴击倍率])));
            successStat = (sbyte)Math.Max(sbyte.MinValue, (Math.Min(sbyte.MaxValue, successStat + rod.Info.Stats[Stat.MaxAC])));

            if (cast)
            {
                DamageItem(rod, 1, true);
            }

            UserItem hook = rod.Slots[(int)FishingSlot.Hook];

            if (hook == null)
            {
                ReceiveChat("需要鱼钩", ChatType.System);
                return;
            }
            else
            {
                DamagedFishingItem(FishingSlot.Hook, 1);
            }

            foreach (UserItem temp in rod.Slots)
            {
                if (temp == null) continue;

                ItemInfo realItem = Functions.GetRealItem(temp.Info, Info.Level, Info.Class, Envir.ItemInfoList);

                switch (realItem.Type)
                {
                    case ItemType.鱼钩:
                        {
                            flexibilityStat = (byte)Math.Max(byte.MinValue, (Math.Min(byte.MaxValue, flexibilityStat + temp.AddedStats[Stat.暴击倍率] + realItem.Stats[Stat.暴击倍率])));
                        }
                        break;
                    case ItemType.鱼漂:
                        {
                            nibbleMin = (byte)Math.Max(byte.MinValue, (Math.Min(byte.MaxValue, nibbleMin + realItem.Stats[Stat.MinAC])));
                            nibbleMax = (byte)Math.Max(byte.MinValue, (Math.Min(byte.MaxValue, nibbleMax + realItem.Stats[Stat.MaxAC])));
                        }
                        break;
                    case ItemType.鱼饵:
                        {
                            successStat = (sbyte)Math.Max(sbyte.MinValue, (Math.Min(sbyte.MaxValue, successStat + realItem.Stats[Stat.MaxAC])));
                        }
                        break;
                    case ItemType.探鱼器:
                        {
                            failedAddSuccessMin = (byte)Math.Max(byte.MinValue, (Math.Min(byte.MaxValue, failedAddSuccessMin + realItem.Stats[Stat.MinAC])));
                            failedAddSuccessMax = (byte)Math.Max(byte.MinValue, (Math.Min(byte.MaxValue, failedAddSuccessMax + realItem.Stats[Stat.MaxAC])));
                        }
                        break;
                    case ItemType.摇轮:
                        {
                            FishingAutoReelChance = (sbyte)Math.Max(sbyte.MinValue, (Math.Min(sbyte.MaxValue, FishingAutoReelChance + realItem.Stats[Stat.MaxMAC])));
                            successStat = (sbyte)Math.Max(sbyte.MinValue, (Math.Min(sbyte.MaxValue, successStat + realItem.Stats[Stat.MaxAC])));
                        }
                        break;
                    default:
                        break;
                }
            }

            FishingNibbleChance = 5 + Envir.Random.Next(nibbleMin, nibbleMax);

            if (cast) FishingChance = Settings.FishingSuccessStart + (int)successStat + (FishingChanceCounter != 0 ? Envir.Random.Next(failedAddSuccessMin, failedAddSuccessMax) : 0) + (FishingChanceCounter * Settings.FishingSuccessMultiplier); //10 //10
            if (FishingChanceCounter != 0) DamagedFishingItem(FishingSlot.Finder, 1);
            FishingChance += Stats[Stat.钓鱼成功数率];

            FishingChance = Math.Min(100, Math.Max(0, FishingChance));
            FishingNibbleChance = Math.Min(100, Math.Max(0, FishingNibbleChance));
            FishingAutoReelChance = Math.Min(100, Math.Max(0, FishingAutoReelChance));

            FishingTime = Envir.Time + FishingCastDelay + Settings.FishingDelay;

            if (cast)
            {
                if (Fishing) return;

                _fishCounter = 0;
                FishFound = false;

                UserItem item = GetBait(1);

                if (item == null)
                {
                    ReceiveChat("需要鱼饵", ChatType.System);
                    return;
                }

                ConsumeItem(item, 1);
                Fishing = true;
            }
            else
            {
                if (!Fishing)
                {
                    Enqueue(GetFishInfo());
                    return;
                }

                Fishing = false;

                if (FishingProgress > 99)
                {
                    FishingChanceCounter++;
                }

                if (FishFound)
                {
                    int getChance = FishingChance + Envir.Random.Next(10, 24) + (FishingProgress > 50 ? flexibilityStat / 2 : 0);
                    getChance = Math.Min(100, Math.Max(0, getChance));

                    if (Envir.Random.Next(0, 100) <= getChance)
                    {
                        FishingChanceCounter = 0;

                        UserItem dropItem = null;
                        UserItem reel = rod.Slots[(int)FishingSlot.Reel];

                        foreach (DropInfo drop in Envir.FishingDrops.Where(x => x.Type == fishingCell.FishingAttribute))
                        {
                            var reward = drop.AttemptDrop(EXPOwner?.Stats[Stat.物品掉落数率] ?? 0, EXPOwner?.Stats[Stat.金币收益数率] ?? 0);

                            if (reward != null)
                            {
                                foreach (var dropitems in reward.Items)
                                {
                                    dropItem = Envir.CreateDropItem(drop.Item);
                                    break;
                                }
                            }
                        }

                        if (dropItem == null)
                        {
                            ReceiveChat("鱼饵被吃光了!", ChatType.System);
                        }
                        else if (FreeSpace(Info.Inventory) < 1)
                        {
                            ReceiveChat(GameLanguage.NoBagSpace, ChatType.System);
                            cancel = true;
                        }
                        else
                        {
                            GainItem(dropItem);
                            Report.ItemChanged(dropItem, dropItem.Count, 2);
                        }

                        if (Envir.Random.Next(100 - Settings.FishingMobSpawnChance) == 0)
                        {
                            MonsterObject mob = MonsterObject.GetMonster(Envir.GetMonsterInfo(Settings.FishingMonster));

                            if (mob == null) return;

                            mob.Spawn(CurrentMap, Back);
                        }

                        DamagedFishingItem(FishingSlot.Reel, 1);

                        if (!FishingAutocast || (FishingAutocast && reel.CurrentDura == 0))
                        {
                            cancel = true;
                        }
                    }
                    else
                    {
                        ReceiveChat("鱼脱钩了", ChatType.System);
                    }
                }

                FishFound = false;
                FishFirstFound = false;
            }

            Enqueue(GetFishInfo());
            Broadcast(GetFishInfo());

            if (FishingAutocast && !cast && !cancel)
            {
                FishingTime = Envir.Time + (FishingCastDelay * 2);
                FishingFoundTime = Envir.Time;
                FishingAutoReelChance = 0;
                FishingNibbleChance = 0;
                FishFirstFound = false;

                FishingCast(true);
            }
        }
        public void FishingChangeAutocast(bool autoCast)
        {
            UserItem rod = Info.Equipment[(int)EquipmentSlot.武器];

            if (rod == null || !rod.Info.IsFishingRod) return;

            UserItem reel = rod.Slots[(int)FishingSlot.Reel];

            if (reel == null)
            {
                FishingAutocast = false;
                return;
            }

            FishingAutocast = autoCast;
        }
        public void UpdateFish()
        {
            if (FishFound != true && FishFirstFound != true)
            {
                FishFound = Envir.Random.Next(0, 100) <= FishingNibbleChance;
                FishingFoundTime = FishFound ? Envir.Time + 3000 : Envir.Time;

                if (FishFound)
                {
                    FishFirstFound = true;
                    DamagedFishingItem(FishingSlot.Float, 1);
                }
            }
            else
            {
                if (FishingAutoReelChance != 0 && Envir.Random.Next(0, 100) <= FishingAutoReelChance)
                {
                    FishingCast(false);
                }
            }

            if (FishingFoundTime < Envir.Time)
                FishFound = false;

            FishingTime = Envir.Time + FishingDelay;

            Enqueue(GetFishInfo());

            if (FishingProgress > 100)
            {
                FishingCast(false);
            }
        }
        Packet GetFishInfo()
        {
            FishingProgress = _fishCounter > 0 ? (int)(((decimal)_fishCounter / FishingProgressMax) * 100) : 0;

            return new S.FishingUpdate
            {
                ObjectID = ObjectID,
                Fishing = Fishing,
                ProgressPercent = FishingProgress,
                FishingPoint = Functions.PointMove(CurrentLocation, Direction, 3),
                ChancePercent = FishingChance,
                FoundFish = FishFound
            };
        }

        #endregion

        #region Quests

        public void AcceptQuest(int index)
        {
            bool canAccept = true;

            if (CurrentQuests.Exists(e => e.Index == index)) return; //e.Info.NpcIndex == npcIndex && 

            QuestInfo info = Envir.QuestInfoList.FirstOrDefault(d => d.Index == index);

            NPCObject npc = null;

            for (int i = CurrentMap.NPCs.Count - 1; i >= 0; i--)
            {
                if (CurrentMap.NPCs[i].ObjectID != info.NpcIndex) continue;

                if (!Functions.InRange(CurrentMap.NPCs[i].CurrentLocation, CurrentLocation, Globals.DataRange)) break;
                npc = CurrentMap.NPCs[i];
                break;
            }
            if (npc == null || !npc.VisibleLog[Info.Index] || !npc.Visible) return;

            if (!info.CanAccept(this))
            {
                canAccept = false;
            }

            if (CurrentQuests.Count >= Globals.MaxConcurrentQuests)
            {
                ReceiveChat("已完成任务的最大数量", ChatType.System);
                return;
            }

            if (CompletedQuests.Contains(index))
            {
                ReceiveChat("任务已经完成", ChatType.System);
                return;
            }

            //check previous chained quests have been completed
            QuestInfo tempInfo = info;
            while (tempInfo != null && tempInfo.RequiredQuest != 0)
            {
                if (!CompletedQuests.Contains(tempInfo.RequiredQuest))
                {
                    canAccept = false;
                    break;
                }

                tempInfo = Envir.QuestInfoList.FirstOrDefault(d => d.Index == tempInfo.RequiredQuest);
            }

            if (!canAccept)
            {
                ReceiveChat("无法接受任务", ChatType.System);
                return;
            }

            if (info.CarryItems.Count > 0)
            {
                foreach (QuestItemTask carryItem in info.CarryItems)
                {
                    ushort count = carryItem.Count;

                    while (count > 0)
                    {
                        UserItem item = Envir.CreateFreshItem(carryItem.Item);

                        if (item.Info.StackSize > count)
                        {
                            item.Count = count;
                            count = 0;
                        }
                        else
                        {
                            count -= item.Info.StackSize;
                            item.Count = item.Info.StackSize;
                        }

                        if (!CanGainQuestItem(item))
                        {
                            RecalculateQuestBag();
                            return;
                        }

                        GainQuestItem(item);

                        Report.ItemChanged(item, item.Count, 2);
                    }
                }
            }

            QuestProgressInfo quest = new QuestProgressInfo(index);

            quest.Init(this);
           
            SendUpdateQuest(quest, QuestState.Add, true);

            CallDefaultNPC(DefaultNPCType.OnAcceptQuest, index);
        }

        public void FinishQuest(int questIndex, int selectedItemIndex = -1)
        {
            QuestProgressInfo quest = CurrentQuests.FirstOrDefault(e => e.Info.Index == questIndex);

            if (quest == null || !quest.Completed) return;

            NPCObject npc = null;

            for (int i = CurrentMap.NPCs.Count - 1; i >= 0; i--)
            {
                if (CurrentMap.NPCs[i].ObjectID != quest.Info.FinishNpcIndex) continue;

                if (!Functions.InRange(CurrentMap.NPCs[i].CurrentLocation, CurrentLocation, Globals.DataRange)) break;
                npc = CurrentMap.NPCs[i];
                break;
            }
            if (npc == null || !npc.VisibleLog[Info.Index] || !npc.Visible) return;

            List<UserItem> rewardItems = new List<UserItem>();

            foreach (var reward in quest.Info.FixedRewards)
            {
                ushort count = reward.Count;

                UserItem rewardItem;

                while (count > 0)
                {
                    rewardItem = Envir.CreateFreshItem(reward.Item);
                    if (reward.Item.StackSize >= count)
                    {
                        rewardItem.Count = count;
                        count = 0;
                    }
                    else
                    {
                        rewardItem.Count = reward.Item.StackSize;
                        count -= reward.Item.StackSize;
                    }

                    rewardItems.Add(rewardItem);
                }
            }

            if (selectedItemIndex >= 0)
            {
                for (int i = 0; i < quest.Info.SelectRewards.Count; i++)
                {
                    if (selectedItemIndex != i) continue;

                    ushort count = quest.Info.SelectRewards[i].Count;
                    UserItem rewardItem;

                    while (count > 0)
                    {
                        rewardItem = Envir.CreateFreshItem(quest.Info.SelectRewards[i].Item);
                        if (quest.Info.SelectRewards[i].Item.StackSize >= count)
                        {
                            rewardItem.Count = count;
                            count = 0;
                        }
                        else
                        {
                            rewardItem.Count = quest.Info.SelectRewards[i].Item.StackSize;
                            count -= quest.Info.SelectRewards[i].Item.StackSize;
                        }

                        rewardItems.Add(rewardItem);
                    }
                }
            }

            if (!CanGainItems(rewardItems.ToArray()))
            {
                ReceiveChat("背包已满，清理后再提交任务", ChatType.System);
                return;
            }

            if (quest.Info.Type != QuestType.重复)
            {
                Info.CompletedQuests.Add(quest.Index);
                GetCompletedQuests();
            }

            SendUpdateQuest(quest, QuestState.Remove);

            if (quest.Info.CarryItems.Count > 0)
            {
                foreach (QuestItemTask carryItem in quest.Info.CarryItems)
                {
                    TakeQuestItem(carryItem.Item, carryItem.Count);
                }
            }

            foreach (QuestItemTask iTask in quest.Info.ItemTasks)
            {
                TakeQuestItem(iTask.Item, iTask.Count);
            }

            foreach (UserItem item in rewardItems)
            {
                GainItem(item);
            }

            RecalculateQuestBag();

            GainGold(quest.Info.GoldReward);
            GainExp(quest.Info.ExpReward);
            GainCredit(quest.Info.CreditReward);

            CallDefaultNPC(DefaultNPCType.OnFinishQuest, questIndex);
        }
        public void AbandonQuest(int questIndex)
        {
            QuestProgressInfo quest = CurrentQuests.FirstOrDefault(e => e.Info.Index == questIndex);

            if (quest == null) return;
 
            SendUpdateQuest(quest, QuestState.Remove);

            RecalculateQuestBag();
        }
        public void ShareQuest(int questIndex)
        {
            bool shared = false;

            if (GroupMembers != null)
            {
                foreach (PlayerObject player in GroupMembers.
                    Where(player => player.CurrentMap == CurrentMap &&
                        Functions.InRange(player.CurrentLocation, CurrentLocation, Globals.DataRange) &&
                        !player.Dead && player != this))
                {
                    player.Enqueue(new S.ShareQuest { QuestIndex = questIndex, SharerName = Name });
                    shared = true;
                }
            }

            if (!shared)
            {
                ReceiveChat("任务无法共享", ChatType.System);
            }
        }

        public void CheckGroupQuestKill(MonsterInfo mInfo)
        {
            if (GroupMembers != null)
            {
                foreach (PlayerObject player in GroupMembers.
                    Where(player => player.CurrentMap == CurrentMap &&
                        Functions.InRange(player.CurrentLocation, CurrentLocation, Globals.DataRange) &&
                        !player.Dead))
                {
                    player.CheckNeedQuestKill(mInfo);
                }
            }
            else
                CheckNeedQuestKill(mInfo);
        }
        public override bool CheckGroupQuestItem(UserItem item, bool gainItem = true)
        {
            bool itemCollected = false;

            if (GroupMembers != null)
            {
                foreach (PlayerObject player in GroupMembers.
                    Where(player => player != null && player.Node != null && player.CurrentMap == CurrentMap &&
                        Functions.InRange(player.CurrentLocation, CurrentLocation, Globals.DataRange) &&
                        !player.Dead))
                {
                    if (player.CheckNeedQuestItem(item, gainItem))
                    {
                        itemCollected = true;
                        player.Report.ItemChanged(item, item.Count, 2, "CheckGroupQuestItem (WinQuestItem)");
                    }
                }
            }
            else
            {
                if (CheckNeedQuestItem(item, gainItem))
                {
                    itemCollected = true;
                    Report.ItemChanged(item, item.Count, 2, "CheckGroupQuestItem (WinQuestItem)");
                }
            }

            return itemCollected;
        }

        public bool CheckNeedQuestItem(UserItem item, bool gainItem = true)
        {
            foreach (QuestProgressInfo quest in CurrentQuests.
                Where(e => e.ItemTaskCount.Count > 0).
                Where(e => e.NeedItem(item.Info)).
                Where(e => CanGainQuestItem(item)))
            {
                if (gainItem)
                {
                    GainQuestItem(item);
                    quest.ProcessItem(Info.QuestInventory);

                    Enqueue(new S.SendOutputMessage { Message = string.Format("任务获得 {0}", item.FriendlyName), Type = OutputMessageType.Quest });

                    SendUpdateQuest(quest, QuestState.Update);

                    Report.ItemChanged(item, item.Count, 2, "CheckNeedQuestItem (WinQuestItem)");
                }
                return true;
            }

            return false;
        }
        public bool CheckNeedQuestFlag(int flagNumber)
        {
            foreach (QuestProgressInfo quest in CurrentQuests.
                Where(e => e.FlagTaskSet.Count > 0).
                Where(e => e.NeedFlag(flagNumber)))
            {
                quest.ProcessFlag(Info.Flags);

                //Enqueue(new S.SendOutputMessage { Message = string.Format("Location visited."), Type = OutputMessageType.Quest });

                SendUpdateQuest(quest, QuestState.Update);
                return true;
            }

            return false;
        }
        public void CheckNeedQuestKill(MonsterInfo mInfo)
        {
            foreach (QuestProgressInfo quest in CurrentQuests.
                    Where(e => e.KillTaskCount.Count > 0).
                    Where(quest => quest.NeedKill(mInfo)))
            {
                quest.ProcessKill(mInfo);

                Enqueue(new S.SendOutputMessage { Message = string.Format("任务猎杀 {0}", mInfo.GameName), Type = OutputMessageType.Quest });

                SendUpdateQuest(quest, QuestState.Update);
            }
        }

        public void RecalculateQuestBag()
        {
            for (int i = Info.QuestInventory.Length - 1; i >= 0; i--)
            {
                UserItem itm = Info.QuestInventory[i];

                if (itm == null) continue;

                bool itemRequired = false;
                bool isCarryItem = false;

                foreach (QuestProgressInfo quest in CurrentQuests)
                {
                    foreach (QuestItemTask carryItem in quest.Info.CarryItems)
                    {
                        if (carryItem.Item == itm.Info)
                        {
                            isCarryItem = true;
                            break;
                        }
                    }

                    foreach (QuestItemTask task in quest.Info.ItemTasks)
                    {
                        if (task.Item == itm.Info)
                        {
                            itemRequired = true;
                            break;
                        }
                    }
                }

                if (!itemRequired && !isCarryItem)
                {
                    Info.QuestInventory[i] = null;
                    Enqueue(new S.DeleteQuestItem { UniqueID = itm.UniqueID, Count = itm.Count });
                }
            }
        }

        public void SendUpdateQuest(QuestProgressInfo quest, QuestState state, bool trackQuest = false)
        {
            quest.CheckCompleted();

            switch (state)
            {
                case QuestState.Add:
                    if (!CurrentQuests.Contains(quest))
                    {
                        CurrentQuests.Add(quest);
                    }
                    quest.SetTimer();
                    break;
                case QuestState.Remove:
                    if (CurrentQuests.Contains(quest))
                    {
                        CurrentQuests.Remove(quest);
                    }
                    quest.RemoveTimer();
                    break;
            }

            Enqueue(new S.ChangeQuest
            {
                Quest = quest.CreateClientQuestProgress(),
                QuestState = state,
                TrackQuest = trackQuest
            });
        }

        public void GetCompletedQuests()
        {
            Enqueue(new S.CompleteQuest
            {
                CompletedQuests = CompletedQuests
            });
        }

        #endregion

        #region Mail

        public void SendMail(string name, string message)
        {
            if (Envir.Time < NextMailTime)
            {
                //not even an error: users shouldnt be sending mails so fast only bots do.
                return;
            }

            NextMailTime = Envir.Time + 10000;

            if (message.Length > 500)
            {
                ReceiveChat(string.Format("很抱歉，已超出邮件容量限制"), ChatType.System);
                return;
            }

            CharacterInfo player = Envir.GetCharacterInfo(name);

            if (player == null)
            {
                ReceiveChat(string.Format(GameLanguage.CouldNotFindPlayer, name), ChatType.System);
                return;
            }

            if (player.Mail.Count > 50)
            {
                ReceiveChat("收件人邮箱已满", ChatType.System);
                return;
            }

            if (player.Friends.Any(e => e.Info == Info && e.Blocked))
            {
                ReceiveChat("邮件未被接收", ChatType.System);
                return;
            }

            if (Info.Friends.Any(e => e.Info == player && e.Blocked))
            {
                ReceiveChat("不能给黑名单里的玩家发邮件", ChatType.System);
                return;
            }

            //sent from player
            MailInfo mail = new MailInfo(player.Index, true)
            {
                Sender = Info.Name,
                Message = message,
                Gold = 0
            };

            mail.Send();
        }

        public void SendMail(string name, string message, uint gold, ulong[] items, bool stamped)
        {
            if (Envir.Time < NextMailTime)
            {
                //not even an error: users shouldnt be sending mails so fast only bots do.
                return;
            }

            NextMailTime = Envir.Time + 10000;

            if (message.Length > 500)
            {
                ReceiveChat(string.Format("很抱歉，已超出邮件容量限制"), ChatType.System);
                return;
            }

            CharacterInfo player = Envir.GetCharacterInfo(name);

            if (player == null)
            {
                ReceiveChat(string.Format(GameLanguage.CouldNotFindPlayer, name), ChatType.System);
                return;
            }

            if (player.Mail.Count > 50)
            {
                ReceiveChat("收件人邮箱已满", ChatType.System);
                return;
            }

            bool hasStamp = false;
            uint totalGold = 0;
            uint parcelCost = GetMailCost(items, gold, stamped);

            totalGold = gold + parcelCost;

            if (Account.Gold < totalGold || Account.Gold < gold || gold > totalGold)
            {
                Enqueue(new S.MailSent { Result = -1 });
                return;
            }

            //Validate user has stamp
            if (stamped)
            {
                for (int i = 0; i < Info.Inventory.Length; i++)
                {
                    UserItem item = Info.Inventory[i];

                    if (item == null || item.Info.Type != ItemType.杂物 || item.Info.Shape != 1 || item.Count < 1) continue;

                    hasStamp = true;

                    if (item.Count > 1) item.Count--;
                    else Info.Inventory[i] = null;

                    Enqueue(new S.DeleteItem { UniqueID = item.UniqueID, Count = 1 });
                    break;
                }
            }

            List<UserItem> giftItems = new List<UserItem>();

            for (int j = 0; j < (hasStamp ? 5 : 1); j++)
            {
                if (items[j] < 1) continue;

                for (int i = 0; i < Info.Inventory.Length; i++)
                {
                    UserItem item = Info.Inventory[i];

                    if (item == null || items[j] != item.UniqueID) continue;

                    if(item.Info.Bind.HasFlag(BindMode.DontTrade))
                    {
                        ReceiveChat(string.Format("{0} 无法邮寄", item.FriendlyName), ChatType.System);
                        return;
                    }

                    if (item.Info.Bind.HasFlag(BindMode.NoMail))
                    {
                        ReceiveChat(string.Format("{0} 无法邮寄", item.FriendlyName), ChatType.System);
                        Enqueue(new S.MailSent { Result = -1 });
                        return;
                    }

                    if (item.RentalInformation != null && item.RentalInformation.BindingFlags.HasFlag(BindMode.DontTrade))
                    {
                        ReceiveChat(string.Format("{0} 无法邮寄", item.FriendlyName), ChatType.System);
                        return;
                    }

                    giftItems.Add(item);

                    Info.Inventory[i] = null;
                    Enqueue(new S.DeleteItem { UniqueID = item.UniqueID, Count = item.Count });
                }
            }

            if (totalGold > 0)
            {
                Account.Gold -= totalGold;
                Enqueue(new S.LoseGold { Gold = totalGold });
            }

            //Create parcel
            MailInfo mail = new MailInfo(player.Index, true)
            {
                MailID = ++Envir.NextMailID,
                Sender = Info.Name,
                Message = message,
                Gold = gold,
                Items = giftItems
            };

            mail.Send();

            Enqueue(new S.MailSent { Result = 1 });
        }

        public void ReadMail(ulong mailID)
        {
            MailInfo mail = Info.Mail.SingleOrDefault(e => e.MailID == mailID);

            if (mail == null) return;

            mail.DateOpened = Envir.Now;

            GetMail();
        }

        public void CollectMail(ulong mailID)
        {
            MailInfo mail = Info.Mail.SingleOrDefault(e => e.MailID == mailID);

            if (mail == null) return;

            if (!mail.Collected)
            {
                ReceiveChat("邮件必须到客栈领取", ChatType.System);
                return;
            }

            if (mail.Items.Count > 0)
            {
                if (!CanGainItems(mail.Items.ToArray()))
                {
                    ReceiveChat("背包以满不能再接收物品", ChatType.System);
                    return;
                }

                for (int i = 0; i < mail.Items.Count; i++)
                {
                    GainItem(mail.Items[i]);
                }
            }

            if (mail.Gold > 0)
            {
                uint gold = mail.Gold;

                if (gold + Account.Gold >= uint.MaxValue)
                    gold = uint.MaxValue - Account.Gold;

                GainGold(gold);
            }

            mail.Items = new List<UserItem>();
            mail.Gold = 0;

            mail.Collected = true;

            Enqueue(new S.ParcelCollected { Result = 1 });

            GetMail();
        }

        public void DeleteMail(ulong mailID)
        {
            MailInfo mail = Info.Mail.SingleOrDefault(e => e.MailID == mailID);

            if (mail == null) return;

            Info.Mail.Remove(mail);

            GetMail();
        }

        public void LockMail(ulong mailID, bool lockMail)
        {
            MailInfo mail = Info.Mail.SingleOrDefault(e => e.MailID == mailID);

            if (mail == null) return;

            mail.Locked = lockMail;

            GetMail();
        }

        public uint GetMailCost(ulong[] items, uint gold, bool stamped)
        {
            uint cost = 0;

            if (!Settings.MailFreeWithStamp || !stamped)
            {
                if (gold > 0 && Settings.MailCostPer1KGold > 0)
                {
                    cost += (uint)Math.Floor((decimal)gold / 1000) * Settings.MailCostPer1KGold;
                }

                if (items != null && items.Length > 0 && Settings.MailItemInsurancePercentage > 0)
                {
                    for (int j = 0; j < (stamped ? 5 : 1); j++)
                    {
                        if (items[j] < 1) continue;

                        for (int i = 0; i < Info.Inventory.Length; i++)
                        {
                            UserItem item = Info.Inventory[i];

                            if (item == null || items[j] != item.UniqueID) continue;

                            cost += (uint)Math.Floor((double)item.Price() / 100 * Settings.MailItemInsurancePercentage);
                        }
                    }
                }
            }


            return cost;
        }

        public void GetMail()
        {
            List<ClientMail> mail = new List<ClientMail>();

            int start = (Info.Mail.Count - Settings.MailCapacity) > 0 ? (Info.Mail.Count - (int)Settings.MailCapacity) : 0;

            for (int i = start; i < Info.Mail.Count; i++)
            {
                foreach (UserItem itm in Info.Mail[i].Items)
                {
                    CheckItem(itm);
                }

                mail.Add(Info.Mail[i].CreateClientMail());
            }

            //foreach (MailInfo m in Info.Mail)
            //{
            //    foreach (UserItem itm in m.Items)
            //    {
            //        CheckItem(itm);
            //    }

            //    mail.Add(m.CreateClientMail());
            //}

            NewMail = false;

            Enqueue(new S.ReceiveMail { Mail = mail });
        }

        public int GetMailAwaitingCollectionAmount()
        {
            int count = 0;
            for (int i = 0; i < Info.Mail.Count; i++)
            {
                if (!Info.Mail[i].Collected) count++;
            }

            return count;
        }

        #endregion

        #region IntelligentCreatures

        public void SummonIntelligentCreature(IntelligentCreatureType pType)
        {
            if (pType == IntelligentCreatureType.None) return;

            if (Dead) return;

            if (CreatureSummoned == true || SummonedCreatureType != IntelligentCreatureType.None) return;

            for (int i = 0; i < Info.IntelligentCreatures.Count; i++)
            {
                if (Info.IntelligentCreatures[i].PetType != pType) continue;

                MonsterInfo mInfo = Envir.GetMonsterInfo(970, (byte)pType);
                if (mInfo == null) return;

                MonsterObject monster = MonsterObject.GetMonster(mInfo);

                if (monster == null) return;
                monster.PetLevel = 0;
                monster.Master = this;
                monster.MaxPetLevel = 7;
                monster.Direction = Direction;
                monster.ActionTime = Envir.Time + 1000;

                var pet = (IntelligentCreatureObject)monster;

                pet.CreatureInfo = Info.IntelligentCreatures[i];
                pet.CreatureRules = new IntelligentCreatureRules
                {
                    MinimalFullness = Info.IntelligentCreatures[i].Info.MinimalFullness,
                    MousePickupEnabled = Info.IntelligentCreatures[i].Info.MousePickupEnabled,
                    MousePickupRange = Info.IntelligentCreatures[i].Info.MousePickupRange,
                    AutoPickupEnabled = Info.IntelligentCreatures[i].Info.AutoPickupEnabled,
                    AutoPickupRange = Info.IntelligentCreatures[i].Info.AutoPickupRange,
                    SemiAutoPickupEnabled = Info.IntelligentCreatures[i].Info.SemiAutoPickupEnabled,
                    SemiAutoPickupRange = Info.IntelligentCreatures[i].Info.SemiAutoPickupRange,
                    CanProduceBlackStone = Info.IntelligentCreatures[i].Info.CanProduceBlackStone
                };

                if (!CurrentMap.ValidPoint(Front)) return;
                monster.Spawn(CurrentMap, Front);
                Pets.Add(monster);

                CreatureSummoned = true;
                SummonedCreatureType = pType;

                ReceiveChat((string.Format("成功召唤灵物 {0}", Info.IntelligentCreatures[i].CustomName)), ChatType.System);
                break;
            }

            //update client
            GetCreaturesInfo();
        }

        public void UnSummonIntelligentCreature(IntelligentCreatureType pType, bool doUpdate = true)
        {
            if (pType == IntelligentCreatureType.None) return;

            for (int i = 0; i < Pets.Count; i++)
            {
                if (Pets[i].Race != ObjectType.Creature) continue;

                var pet = (IntelligentCreatureObject)Pets[i];
                if (pet.PetType != pType) continue;
                if (doUpdate) ReceiveChat(string.Format("成功收回灵物 {0}", pet.CustomName), ChatType.System);

                pet.Die();

                CreatureSummoned = false;
                SummonedCreatureType = IntelligentCreatureType.None;
                break;
            }

            //update client
            if (doUpdate) GetCreaturesInfo();
        }

        public void ReleaseIntelligentCreature(IntelligentCreatureType pType, bool doUpdate = true)
        {
            if (pType == IntelligentCreatureType.None) return;

            //remove creature
            for (int i = 0; i < Info.IntelligentCreatures.Count; i++)
            {
                if (Info.IntelligentCreatures[i].PetType != pType) continue;

                if (doUpdate) ReceiveChat((string.Format("灵物 {0} 已被解雇", Info.IntelligentCreatures[i].CustomName)), ChatType.System);

                Info.IntelligentCreatures.Remove(Info.IntelligentCreatures[i]);
                break;
            }

            //re-arrange slots
            for (int i = 0; i < Info.IntelligentCreatures.Count; i++)
                Info.IntelligentCreatures[i].SlotIndex = i;

            //update client
            if (doUpdate) GetCreaturesInfo();
        }

        public void UpdateSummonedCreature(IntelligentCreatureType pType)
        {
            if (pType == IntelligentCreatureType.None) return;

            UserIntelligentCreature creatureInfo = null;
            for (int i = 0; i < Info.IntelligentCreatures.Count; i++)
            {
                if (Info.IntelligentCreatures[i].PetType != pType) continue;

                creatureInfo = Info.IntelligentCreatures[i];
                break;
            }
            if (creatureInfo == null) return;

            for (int i = 0; i < Pets.Count; i++)
            {
                if (Pets[i].Race != ObjectType.Creature) continue;

                var pet = (IntelligentCreatureObject)Pets[i];
                if (pet.PetType != pType) continue;

                pet.CustomName = creatureInfo.CustomName;
                pet.ItemFilter = creatureInfo.Filter;
                pet.CurrentPickupMode = creatureInfo.petMode;
                break;
            }
        }

        public void RefreshCreaturesTimeLeft()
        {
            if (Info.IntelligentCreatures.Count == 0) return;

            if (Envir.Time > CreatureTimeLeftTicker)
            {
                //ExpireTime
                List<int> releasedPets = new List<int>();
                CreatureTimeLeftTicker = Envir.Time + Settings.Second;

                for (int i = 0; i < Info.IntelligentCreatures.Count; i++)
                {
                    if (Info.IntelligentCreatures[i].Expire == DateTime.MinValue) continue; //permanent
    
                    if (Info.IntelligentCreatures[i].Expire < Envir.Now)
                    {
                        //Info.IntelligentCreatures[i].ExpireTime = 0;

                        if (CreatureSummoned && SummonedCreatureType == Info.IntelligentCreatures[i].PetType)
                        {
                            UnSummonIntelligentCreature(SummonedCreatureType, false);
                        }

                        releasedPets.Add(i);
                    }
                }

                for (int i = (releasedPets.Count - 1); i >= 0; i--)
                {
                    ReceiveChat(string.Format("灵物 {0} 已过期", Info.IntelligentCreatures[releasedPets[i]].CustomName), ChatType.System);
                    ReleaseIntelligentCreature(Info.IntelligentCreatures[releasedPets[i]].PetType, false);
                }

                if (SendIntelligentCreatureUpdates && CreatureSummoned && SummonedCreatureType != IntelligentCreatureType.None)
                {
                    //update client
                    GetCreaturesInfo();
                }
            }
        }

        public void RefreshCreatureSummoned()
        {
            if (SummonedCreatureType == IntelligentCreatureType.None || !CreatureSummoned)
            {
                //make sure both are in the unsummoned state
                CreatureSummoned = false;
                SummonedCreatureType = IntelligentCreatureType.None;
                return;
            }

            bool petFound = false;
            for (int i = 0; i < Pets.Count; i++)
            {
                if (Pets[i].Race != ObjectType.Creature) continue;

                var pet = (IntelligentCreatureObject)Pets[i];
                if (pet.PetType != SummonedCreatureType) continue;
                petFound = true;
                break;
            }

            if (!petFound)
            {
                MessageQueue.EnqueueDebugging(string.Format("{0}: 灵物不存在 {1}", Name, SummonedCreatureType.ToString()));
                CreatureSummoned = false;
                SummonedCreatureType = IntelligentCreatureType.None;
            }
        }

        public void IntelligentCreaturePickup(bool mousemode, Point atlocation)
        {
            if (!CreatureSummoned) return;

            for (int i = 0; i < Pets.Count; i++)
            {
                if (Pets[i].Race != ObjectType.Creature) continue;

                var pet = (IntelligentCreatureObject)Pets[i];
                if (pet.PetType != SummonedCreatureType) continue;

                pet.ManualPickup(mousemode, atlocation);
                break;
            }
        }

        public void IntelligentCreatureGainPearls(int amount)
        {
            Info.PearlCount += amount;
            if (Info.PearlCount > int.MaxValue) Info.PearlCount = int.MaxValue;
        }

        public void IntelligentCreatureLosePearls(int amount)
        {
            Info.PearlCount -= amount;
            if (Info.PearlCount < 0) Info.PearlCount = 0;
        }

        public void IntelligentCreatureProducePearl()
        {
            Info.PearlCount++;
        }
        public bool IntelligentCreatureProduceBlackStone()
        {
            ItemInfo iInfo = Envir.GetItemInfo(Settings.CreatureBlackStoneName);
            if (iInfo == null) return false;

            UserItem item = Envir.CreateDropItem(iInfo);
            item.Count = 1;

            if (!CanGainItem(item))
            {
                MailInfo mail = new MailInfo(Info.Index)
                {
                    MailID = ++Envir.NextMailID,
                    Sender = "黑色铁矿石",
                    Message = "灵物生产出 黑色铁矿石x1，无法存放到背包中",
                    Items = new List<UserItem> { item },
                };

                mail.Send();
                return false;
            }

            GainItem(item);
            return true;
        }

        public void IntelligentCreatureSay(IntelligentCreatureType pType, string message)
        {
            if (!CreatureSummoned || message == "") return;
            if (pType != SummonedCreatureType) return;

            for (int i = 0; i < Pets.Count; i++)
            {
                if (Pets[i].Race != ObjectType.Creature) continue;

                var pet = (IntelligentCreatureObject)Pets[i];
                if (pet.PetType != pType) continue;

                Enqueue(new S.ObjectChat { ObjectID = Pets[i].ObjectID, Text = message, Type = ChatType.Normal });
                return;
            }
        }

        public void StrongboxRewardItem(int boxtype)
        {
            int highRate = int.MaxValue;
            UserItem dropItem = null;

            foreach (DropInfo drop in Envir.StrongboxDrops)
            {
                int rate = (int)(Envir.Random.Next(0, drop.Chance) / Settings.DropRate);
                if (rate < 1) rate = 1;

                if (highRate > rate)
                {
                    highRate = rate;
                    dropItem = Envir.CreateFreshItem(drop.Item);
                }
            }

            if (dropItem == null)
            {
                ReceiveChat("什么也没发现", ChatType.System);
                return;
            }

            if (dropItem.Info.Type == ItemType.灵物 && dropItem.Info.Shape == 26)
            {
                dropItem = CreateDynamicWonderDrug(boxtype, dropItem);
            }
            else
                dropItem = Envir.CreateDropItem(dropItem.Info);

            if (FreeSpace(Info.Inventory) < 1)
            {
                ReceiveChat("空间不足", ChatType.System);
                return;
            }

            if (dropItem != null) GainItem(dropItem);
        }

        public void BlackstoneRewardItem()
        {
            int highRate = int.MaxValue;
            UserItem dropItem = null;
            foreach (DropInfo drop in Envir.BlackstoneDrops)
            {
                int rate = (int)(Envir.Random.Next(0, drop.Chance) / Settings.DropRate); if (rate < 1) rate = 1;

                if (highRate > rate)
                {
                    highRate = rate;
                    dropItem = Envir.CreateDropItem(drop.Item);
                }
            }
            if (FreeSpace(Info.Inventory) < 1)
            {
                ReceiveChat("空间不足", ChatType.System);
                return;
            }
            if (dropItem != null) GainItem(dropItem);
        }

        private UserItem CreateDynamicWonderDrug(int boxtype, UserItem dropitem)
        {
            dropitem.CurrentDura = (ushort)1;//* 3600
            switch ((int)dropitem.Info.Effect)
            {
                case 0://exp low/med/high
                    dropitem.AddedStats[Stat.经验增长数率] = 5;
                    if (boxtype > 0) dropitem.AddedStats[Stat.经验增长数率] = 10;
                    if (boxtype > 1) dropitem.AddedStats[Stat.经验增长数率] = 20;
                    break;
                case 1://drop low/med/high
                    dropitem.AddedStats[Stat.物品掉落数率] = 10;
                    if (boxtype > 0) dropitem.AddedStats[Stat.物品掉落数率] = 20;
                    if (boxtype > 1) dropitem.AddedStats[Stat.物品掉落数率] = 50;
                    break;
                case 2://hp low/med/high
                    dropitem.AddedStats[Stat.HP] = 50;
                    if (boxtype > 0) dropitem.AddedStats[Stat.HP] = 100;
                    if (boxtype > 1) dropitem.AddedStats[Stat.HP] = 200;
                    break;
                case 3://mp low/med/high
                    dropitem.AddedStats[Stat.MP] = 50;
                    if (boxtype > 0) dropitem.AddedStats[Stat.MP] = 100;
                    if (boxtype > 1) dropitem.AddedStats[Stat.MP] = 200;
                    break;
                case 4://ac low/med/high
                    dropitem.AddedStats[Stat.MaxAC] = 1;
                    if (boxtype > 0) dropitem.AddedStats[Stat.MaxAC] = 3;
                    if (boxtype > 1) dropitem.AddedStats[Stat.MaxAC] = 5;
                    break;
                case 5://amc low/med/high
                    dropitem.AddedStats[Stat.MaxMAC] = 1;
                    if (boxtype > 0) dropitem.AddedStats[Stat.MaxMAC] = 3;
                    if (boxtype > 1) dropitem.AddedStats[Stat.MaxMAC] = 5;
                    break;
                case 6://speed low/med/high
                    dropitem.AddedStats[Stat.攻击速度] = 2;
                    if (boxtype > 0) dropitem.AddedStats[Stat.攻击速度] = 3;
                    if (boxtype > 1) dropitem.AddedStats[Stat.攻击速度] = 4;
                    break;
            }

            return dropitem;
        }

        private IntelligentCreatureObject GetCreatureByName(string creatureName)
        {
            if (!CreatureSummoned || creatureName == "") return null;
            if (SummonedCreatureType == IntelligentCreatureType.None) return null;

            for (int i = 0; i < Pets.Count; i++)
            {
                if (Pets[i].Race != ObjectType.Creature) continue;

                var pet = (IntelligentCreatureObject)Pets[i];
                if (pet.PetType != SummonedCreatureType) continue;

                return (pet);
            }
            return null;
        }

        private void GetCreaturesInfo()
        {
            S.UpdateIntelligentCreatureList packet = new S.UpdateIntelligentCreatureList
            {
                CreatureSummoned = CreatureSummoned,
                SummonedCreatureType = SummonedCreatureType,
                PearlCount = Info.PearlCount,
            };

            for (int i = 0; i < Info.IntelligentCreatures.Count; i++)
                packet.CreatureList.Add(Info.IntelligentCreatures[i].CreateClientIntelligentCreature());

            Enqueue(packet);
        }


        #endregion

        #region Friends
        public void AddFriend(string name, bool blocked = false)
        {
            CharacterInfo info = Envir.GetCharacterInfo(name);

            if (info == null)
            {
                ReceiveChat("玩家不存在", ChatType.System);
                return;
            }

            if (Name == name)
            {
                ReceiveChat("不能添加自己为好友", ChatType.System);
                return;
            }

            if (Info.Friends.Any(e => e.Index == info.Index))
            {
                ReceiveChat("已添加玩家", ChatType.System);
                return;
            }

            FriendInfo friend = new FriendInfo(info, blocked);

            Info.Friends.Add(friend);

            GetFriends();
        }

        public void RemoveFriend(int index)
        {
            FriendInfo friend = Info.Friends.FirstOrDefault(e => e.Index == index);

            if (friend == null)
            {
                return;
            }

            Info.Friends.Remove(friend);

            GetFriends();
        }

        public void AddMemo(int index, string memo)
        {
            if (string.IsNullOrEmpty(memo) || memo.Length > 200) return;

            FriendInfo friend = Info.Friends.FirstOrDefault(e => e.Index == index);

            if (friend == null)
            {
                return;
            }

            friend.Memo = memo;

            GetFriends();
        }

        public void GetFriends()
        {
            List<ClientFriend> friends = new List<ClientFriend>();

            foreach (FriendInfo friend in Info.Friends)
            {
                if (friend.Info != null)
                {
                    friends.Add(friend.CreateClientFriend());
                }
            }

            Enqueue(new S.FriendUpdate { Friends = friends });
        }

        #endregion

        #region Refining

        public void DepositRefineItem(int from, int to)
        {

            S.DepositRefineItem p = new S.DepositRefineItem { From = from, To = to, Success = false };

            if (NPCPage == null || !String.Equals(NPCPage.Key, NPCScript.RefineKey, StringComparison.CurrentCultureIgnoreCase))
            {
                Enqueue(p);
                return;
            }
            NPCObject ob = null;
            for (int i = 0; i < CurrentMap.NPCs.Count; i++)
            {
                if (CurrentMap.NPCs[i].ObjectID != NPCObjectID) continue;
                ob = CurrentMap.NPCs[i];
                break;
            }

            if (ob == null || !Functions.InRange(ob.CurrentLocation, CurrentLocation, Globals.DataRange))
            {
                Enqueue(p);
                return;
            }


            if (from < 0 || from >= Info.Inventory.Length)
            {
                Enqueue(p);
                return;
            }

            if (to < 0 || to >= Info.Refine.Length)
            {
                Enqueue(p);
                return;
            }

            UserItem temp = Info.Inventory[from];

            if (temp == null)
            {
                Enqueue(p);
                return;
            }

            if (Info.Refine[to] == null)
            {
                Info.Refine[to] = temp;
                Info.Inventory[from] = null;
                RefreshBagWeight();

                Report.ItemMoved(temp, MirGridType.Inventory, MirGridType.Refine, from, to);

                p.Success = true;
                Enqueue(p);
                return;
            }
            Enqueue(p);

        }
        public void RetrieveRefineItem(int from, int to)
        {
            S.RetrieveRefineItem p = new S.RetrieveRefineItem { From = from, To = to, Success = false };

            if (from < 0 || from >= Info.Refine.Length)
            {
                Enqueue(p);
                return;
            }

            if (to < 0 || to >= Info.Inventory.Length)
            {
                Enqueue(p);
                return;
            }

            UserItem temp = Info.Refine[from];

            if (temp == null)
            {
                Enqueue(p);
                return;
            }

            if (Info.Inventory[to] == null)
            {
                Info.Inventory[to] = temp;
                Info.Refine[from] = null;

                Report.ItemMoved(temp, MirGridType.Refine, MirGridType.Inventory, from, to);

                p.Success = true;
                RefreshBagWeight();
                Enqueue(p);

                return;
            }
            Enqueue(p);
        }
        public void RefineCancel()
        {
            for (int t = 0; t < Info.Refine.Length; t++)
            {
                UserItem temp = Info.Refine[t];

                if (temp == null) continue;

                for (int i = 0; i < Info.Inventory.Length; i++)
                {
                    if (Info.Inventory[i] != null) continue;

                    //Put item back in inventory
                    if (CanGainItem(temp))
                    {
                        RetrieveRefineItem(t, i);
                    }
                    else //Send item via mail if it can no longer be stored
                    {
                        Enqueue(new S.DeleteItem { UniqueID = temp.UniqueID, Count = temp.Count });

                        MailInfo mail = new MailInfo(Info.Index)
                        {
                            MailID = ++Envir.NextMailID,
                            Sender = "Refiner",
                            Message = "精炼被取消，一件物品将无法返还背包",
                            Items = new List<UserItem> { temp },
                        };

                        mail.Send();
                    }

                    Info.Refine[t] = null;

                    break;
                }
            }
        }
        public void RefineItem(ulong uniqueID)
        {
            Enqueue(new S.RepairItem { UniqueID = uniqueID }); //CHECK THIS.

            if (Dead) return;

            if (NPCPage == null || (!String.Equals(NPCPage.Key, NPCScript.RefineKey, StringComparison.CurrentCultureIgnoreCase))) return;

            int index = -1;

            for (int i = 0; i < Info.Inventory.Length; i++)
            {
                if (Info.Inventory[i] == null || Info.Inventory[i].UniqueID != uniqueID) continue;
                index = i;
                break;
            }

            if (index == -1) return;

            if (Info.Inventory[index].RefineAdded != 0)
            {
                ReceiveChat(String.Format("{0} 再次尝试精炼之前，需要进行检查", Info.Inventory[index].FriendlyName), ChatType.System);
                return;
            }

            if ((Info.Inventory[index].Info.Type != ItemType.武器) && (Settings.OnlyRefineWeapon))
            {
                ReceiveChat(String.Format("物品 {0} 不可精炼", Info.Inventory[index].FriendlyName), ChatType.System);
                return;
            }

            if (Info.Inventory[index].Info.Bind.HasFlag(BindMode.DontUpgrade))
            {
                ReceiveChat(String.Format("物品 {0} 不可精炼", Info.Inventory[index].FriendlyName), ChatType.System);
                return;
            }

            if (Info.Inventory[index].RentalInformation != null && Info.Inventory[index].RentalInformation.BindingFlags.HasFlag(BindMode.DontUpgrade))
            {
                ReceiveChat(String.Format("物品 {0} 不可精炼", Info.Inventory[index].FriendlyName), ChatType.System);
                return;
            }


            if (index == -1) return;




            //CHECK GOLD HERE
            uint cost = (uint)((Info.Inventory[index].Info.RequiredAmount * 10) * Settings.RefineCost);

            if (cost > Account.Gold)
            {
                ReceiveChat(String.Format("没有足够的金币完成精炼 {0}", Info.Inventory[index].FriendlyName), ChatType.System);
                return;
            }

            Account.Gold -= cost;
            Enqueue(new S.LoseGold { Gold = cost });

            //START OF FORMULA

            Info.CurrentRefine = Info.Inventory[index];
            Info.Inventory[index] = null;
            Info.CollectTime = (Envir.Time + (Settings.RefineTime * Settings.Minute));
            Enqueue(new S.RefineItem { UniqueID = uniqueID });


            short orePurity = 0;
            byte oreAmount = 0;
            byte itemAmount = 0;
            short totalDC = 0;
            short totalMC = 0;
            short totalSC = 0;
            short requiredLevel = 0;
            short durability = 0;
            short currentDura = 0;
            short addedStats = 0;
            UserItem ingredient;

            for (int i = 0; i < Info.Refine.Length; i++)
            {
                ingredient = Info.Refine[i];

                if (ingredient == null) continue;
                if (ingredient.Info.Type == ItemType.武器)
                {
                    Info.Refine[i] = null;
                    continue;
                }

                if ((ingredient.Info.Stats[Stat.MaxDC] > 0) || (ingredient.Info.Stats[Stat.MaxMC] > 0) || (ingredient.Info.Stats[Stat.MaxSC] > 0))
                {
                    totalDC += (short)(ingredient.Info.Stats[Stat.MinDC] + ingredient.Info.Stats[Stat.MaxDC] + ingredient.AddedStats[Stat.MaxDC]);
                    totalMC += (short)(ingredient.Info.Stats[Stat.MinMC] + ingredient.Info.Stats[Stat.MaxMC] + ingredient.AddedStats[Stat.MaxMC]);
                    totalSC += (short)(ingredient.Info.Stats[Stat.MinSC] + ingredient.Info.Stats[Stat.MaxSC] + ingredient.AddedStats[Stat.MaxSC]);
                    requiredLevel += ingredient.Info.RequiredAmount;
                    if (Math.Floor(ingredient.MaxDura / 1000M) == Math.Floor(ingredient.Info.Durability / 1000M)) durability++;
                    if (Math.Floor(ingredient.CurrentDura / 1000M) == Math.Floor(ingredient.MaxDura / 1000M)) currentDura++;
                    itemAmount++;
                }

                if (ingredient.Info.FriendlyName == Settings.RefineOreName)
                {
                    orePurity += (short)Math.Floor(ingredient.CurrentDura / 1000M);
                    oreAmount++;
                }

                Info.Refine[i] = null;
            }

            if ((totalDC == 0) && (totalMC == 0) && (totalSC == 0))
            {
                Info.CurrentRefine.RefineSuccessChance = 0;
                //Info.CurrentRefine.RefinedValue = RefinedValue.None;
                Info.CurrentRefine.RefineAdded = Settings.RefineIncrease;

                if (Settings.RefineTime == 0)
                {
                    CollectRefine();
                }
                else
                {
                    ReceiveChat(String.Format("你的 {0} 正在精炼中, 可在 {1} 分钟后取回", Info.CurrentRefine.FriendlyName, Settings.RefineTime), ChatType.System);
                }

                return;
            }

            if (oreAmount == 0)
            {
                Info.CurrentRefine.RefineSuccessChance = 0;
                //Info.CurrentRefine.RefinedValue = RefinedValue.None;
                Info.CurrentRefine.RefineAdded = Settings.RefineIncrease;
                if (Settings.RefineTime == 0)
                {
                    CollectRefine();
                }
                else
                {
                    ReceiveChat(String.Format("{0} 正在精炼中, 可在 {1} 分钟后取回", Info.CurrentRefine.FriendlyName, Settings.RefineTime), ChatType.System);
                }
                return;
            }


            short refineStat = 0;

            if ((totalDC > totalMC) && (totalDC > totalSC))
            {
                Info.CurrentRefine.RefinedValue = RefinedValue.DC;
                refineStat = totalDC;
            }

            if ((totalMC > totalDC) && (totalMC > totalSC))
            {
                Info.CurrentRefine.RefinedValue = RefinedValue.MC;
                refineStat = totalMC;
            }

            if ((totalSC > totalDC) && (totalSC > totalMC))
            {
                Info.CurrentRefine.RefinedValue = RefinedValue.SC;
                refineStat = totalSC;
            }

            Info.CurrentRefine.RefineAdded = Settings.RefineIncrease;


            int itemSuccess = 0; //Chance out of 35%

            itemSuccess += (refineStat * 5) - Info.CurrentRefine.Info.RequiredAmount;
            itemSuccess += 5;
            if (itemSuccess > 10) itemSuccess = 10;
            if (itemSuccess < 0) itemSuccess = 0; //10%


            if ((requiredLevel / itemAmount) > (Info.CurrentRefine.Info.RequiredAmount - 5)) itemSuccess += 10; //20%
            if (durability == itemAmount) itemSuccess += 10; //30%
            if (currentDura == itemAmount) itemSuccess += 5; //35%

            int oreSuccess = 0; //Chance out of 35%

            if (oreAmount >= itemAmount) oreSuccess += 15; //15%
            if ((orePurity / oreAmount) >= (refineStat / itemAmount)) oreSuccess += 15; //30%
            if (orePurity == refineStat) oreSuccess += 5; //35%

            int luckSuccess = (Info.CurrentRefine.AddedStats[Stat.幸运] + 5); //Chance out of 10%
            if (luckSuccess > 10) luckSuccess = 10;
            if (luckSuccess < 0) luckSuccess = 0;


            int baseSuccess = Settings.RefineBaseChance; //20% as standard

            int successChance = (itemSuccess + oreSuccess + luckSuccess + baseSuccess);

            addedStats = (byte)(Info.CurrentRefine.AddedStats[Stat.MaxDC] + Info.CurrentRefine.AddedStats[Stat.MaxMC] + Info.CurrentRefine.AddedStats[Stat.MaxSC]);
            if (Info.CurrentRefine.Info.Type == ItemType.武器) addedStats = (short)(addedStats * Settings.RefineWepStatReduce);
            else addedStats = (short)(addedStats * Settings.RefineItemStatReduce);
            if (addedStats > 50) addedStats = 50;

            successChance -= addedStats;

            Info.CurrentRefine.RefineSuccessChance = successChance;

            //END OF FORMULA

            if (Settings.RefineTime == 0)
            {
                CollectRefine();
            }
            else
            {
                ReceiveChat(String.Format("物品 {0} 正在精炼中, 可在 {1} 分钟后取回", Info.CurrentRefine.FriendlyName, Settings.RefineTime), ChatType.System);
            }
        }
        public void CollectRefine()
        {
            S.NPCCollectRefine p = new S.NPCCollectRefine { Success = false };

            if (Info.CurrentRefine == null)
            {
                ReceiveChat("没有任何精炼的物品", ChatType.System);
                Enqueue(p);
                return;
            }

            if (Info.CollectTime > Envir.Time)
            {
                ReceiveChat(string.Format("{0} 将在 {1} 分钟后精炼完成", Info.CurrentRefine.FriendlyName, ((Info.CollectTime - Envir.Time) / Settings.Minute)), ChatType.System);
                Enqueue(p);
                return;
            }

            int index = -1;

            for (int i = 0; i < Info.Inventory.Length; i++)
            {
                if (Info.Inventory[i] != null) continue;
                index = i;
                break;
            }

            if (index == -1)
            {
                ReceiveChat(String.Format("背包空间不足，清理背包后再来取回 {0}", Info.CurrentRefine.FriendlyName), ChatType.System);
                Enqueue(p);
                return;
            }

            ReceiveChat(String.Format("物品精炼完成并返还背包中"), ChatType.System);
            p.Success = true;

            GainItem(Info.CurrentRefine);

            Info.CurrentRefine = null;
            Info.CollectTime = 0;
            Enqueue(p);
        }
        public void CheckRefine(ulong uniqueID)
        {
            if (Dead) return;

            if (NPCPage == null || (!String.Equals(NPCPage.Key, NPCScript.RefineCheckKey, StringComparison.CurrentCultureIgnoreCase))) return;

            int index = -1;

            for (int i = 0; i < Info.Inventory.Length; i++)
            {
                UserItem temp = Info.Inventory[i];
                if (temp == null || temp.UniqueID != uniqueID) continue;
                index = i;
                break;
            }

            if (index == -1) return;

            if (Info.Inventory[index].RefineAdded == 0)
            {
                ReceiveChat(String.Format("{0} 未被精炼无需检查", Info.Inventory[index].FriendlyName), ChatType.System);
                return;
            }

            if (Envir.Random.Next(1, 100) > Info.Inventory[index].RefineSuccessChance)
            {
                Info.Inventory[index].RefinedValue = RefinedValue.None;
            }

            if (Envir.Random.Next(1, 100) < Settings.RefineCritChance)
            {
                Info.Inventory[index].RefineAdded = (byte)(Info.Inventory[index].RefineAdded * Settings.RefineCritIncrease);
            }

            if ((Info.Inventory[index].RefinedValue == RefinedValue.DC) && (Info.Inventory[index].RefineAdded > 0))
            {
                ReceiveChat(String.Format("恭喜 {0} 额外增加 {1}点 物理攻击", Info.Inventory[index].FriendlyName, Info.Inventory[index].RefineAdded), ChatType.System);
                Info.Inventory[index].AddedStats[Stat.MaxDC] = (int)Math.Min(int.MaxValue, Info.Inventory[index].AddedStats[Stat.MaxDC] + Info.Inventory[index].RefineAdded);
                Info.Inventory[index].RefineAdded = 0;
                Info.Inventory[index].RefinedValue = RefinedValue.None;
                Info.Inventory[index].RefineSuccessChance = 0;

            }
            else if ((Info.Inventory[index].RefinedValue == RefinedValue.MC) && (Info.Inventory[index].RefineAdded > 0))
            {
                ReceiveChat(String.Format("恭喜 {0} 额外增加 {1}点 魔法攻击", Info.Inventory[index].FriendlyName, Info.Inventory[index].RefineAdded), ChatType.System);
                Info.Inventory[index].AddedStats[Stat.MaxMC] = (int)Math.Min(int.MaxValue, Info.Inventory[index].AddedStats[Stat.MaxMC] + Info.Inventory[index].RefineAdded);
                Info.Inventory[index].RefineAdded = 0;
                Info.Inventory[index].RefinedValue = RefinedValue.None;
                Info.Inventory[index].RefineSuccessChance = 0;

            }
            else if ((Info.Inventory[index].RefinedValue == RefinedValue.SC) && (Info.Inventory[index].RefineAdded > 0))
            {
                ReceiveChat(String.Format("恭喜 {0} 额外增加 {1}点 道术攻击", Info.Inventory[index].FriendlyName, Info.Inventory[index].RefineAdded), ChatType.System);
                Info.Inventory[index].AddedStats[Stat.MaxSC] = (int)Math.Min(int.MaxValue, Info.Inventory[index].AddedStats[Stat.MaxSC] + Info.Inventory[index].RefineAdded);
                Info.Inventory[index].RefineAdded = 0;
                Info.Inventory[index].RefinedValue = RefinedValue.None;
                Info.Inventory[index].RefineSuccessChance = 0;
            }
            else if ((Info.Inventory[index].RefinedValue == RefinedValue.None) && (Info.Inventory[index].RefineAdded > 0))
            {
                ReceiveChat(String.Format("精炼失败 {0} 已经破碎", Info.Inventory[index].FriendlyName), ChatType.System);
                Enqueue(new S.RefineItem { UniqueID = Info.Inventory[index].UniqueID });
                Info.Inventory[index].RefineSuccessChance = 0;
                Info.Inventory[index] = null;
                return;
            }

            Enqueue(new S.ItemUpgraded { Item = Info.Inventory[index] });
            return;
        }

        #endregion

        #region Relationship

        public void NPCDivorce()
        {
            if (Info.Married == 0)
            {
                ReceiveChat(string.Format("未婚"), ChatType.System);
                return;
            }

            CharacterInfo lover = Envir.GetCharacterInfo(Info.Married);
            PlayerObject player = Envir.GetPlayer(lover.Name);

            Info.Married = 0;
            Info.MarriedDate = Envir.Now;

            if (Info.Equipment[(int)EquipmentSlot.左戒指] != null)
            {
                Info.Equipment[(int)EquipmentSlot.左戒指].WeddingRing = -1;
                Enqueue(new S.RefreshItem { Item = Info.Equipment[(int)EquipmentSlot.左戒指] });
            }

            GetRelationship(false);
            
            lover.Married = 0;
            lover.MarriedDate = Envir.Now;
            if (lover.Equipment[(int)EquipmentSlot.左戒指] != null)
                lover.Equipment[(int)EquipmentSlot.左戒指].WeddingRing = -1;

            if (player != null)
            {
                player.GetRelationship(false);
                player.ReceiveChat(string.Format("强制离婚生效"), ChatType.System);
                if (player.Info.Equipment[(int)EquipmentSlot.左戒指] != null)
                    player.Enqueue(new S.RefreshItem { Item = player.Info.Equipment[(int)EquipmentSlot.左戒指] });
            }
        }

        public bool CheckMakeWeddingRing()
        {
            if (Info.Married == 0)
            {
                ReceiveChat(string.Format("结婚后才能制作结婚戒指"), ChatType.System);
                return false;
            }

            if (Info.Equipment[(int)EquipmentSlot.左戒指] == null)
            {
                ReceiveChat(string.Format("左手位需佩戴要铭刻的结婚戒指"), ChatType.System);
                return false;
            }

            if (Info.Equipment[(int)EquipmentSlot.左戒指].WeddingRing != -1)
            {
                ReceiveChat(string.Format("结婚戒指铭刻成功"), ChatType.System);
                return false;
            }

            if (Info.Equipment[(int)EquipmentSlot.左戒指].Info.Bind.HasFlag(BindMode.NoWeddingRing))
            {
                ReceiveChat(string.Format("婚戒禁用此类戒指"), ChatType.System);
                return false;
            }

            return true;
        }

        public void MakeWeddingRing()
        {
            if (CheckMakeWeddingRing())
            {
                Info.Equipment[(int)EquipmentSlot.左戒指].WeddingRing = Info.Married;
                Enqueue(new S.RefreshItem { Item = Info.Equipment[(int)EquipmentSlot.左戒指] });
            }
        }

        public void ReplaceWeddingRing(ulong uniqueID)
        {
            if (Dead) return;

            if (NPCPage == null || (!String.Equals(NPCPage.Key, NPCScript.ReplaceWedRingKey, StringComparison.CurrentCultureIgnoreCase))) return;

            UserItem temp = null;
            UserItem CurrentRing = Info.Equipment[(int)EquipmentSlot.左戒指];

            if (CurrentRing == null)
            {
                ReceiveChat(string.Format("需要佩戴结婚戒指"), ChatType.System);
                return;
            }

            if (CurrentRing.WeddingRing == -1)
            {
                ReceiveChat(string.Format("左戒指非结婚戒指不能完成替换"), ChatType.System);
                return;
            }

            int index = -1;

            for (int i = 0; i < Info.Inventory.Length; i++)
            {
                temp = Info.Inventory[i];
                if (temp == null || temp.UniqueID != uniqueID) continue;
                index = i;
                break;
            }

            if (index == -1) return;

            temp = Info.Inventory[index];


            if (temp.Info.Type != ItemType.戒指)
            {
                ReceiveChat(string.Format("该物品无法替换结婚戒指"), ChatType.System);
                return;
            }

            if (!CanEquipItem(temp, (int)EquipmentSlot.左戒指))
            {
                ReceiveChat(string.Format("无法装备试图使用的物品"), ChatType.System);
                return;
            }

            if (temp.Info.Bind.HasFlag(BindMode.NoWeddingRing))
            {
                ReceiveChat(string.Format("此类戒指不能替换结婚戒指"), ChatType.System);
                return;
            }

            uint cost = (uint)((Info.Inventory[index].Info.RequiredAmount * 10) * Settings.ReplaceWedRingCost);

            if (cost > Account.Gold)
            {
                ReceiveChat(String.Format("没有足够的金币来替换结婚戒指"), ChatType.System);
                return;
            }

            Account.Gold -= cost;
            Enqueue(new S.LoseGold { Gold = cost });


            temp.WeddingRing = Info.Married;
            CurrentRing.WeddingRing = -1;

            Info.Equipment[(int)EquipmentSlot.左戒指] = temp;
            Info.Inventory[index] = CurrentRing;

            Enqueue(new S.EquipItem { Grid = MirGridType.Inventory, UniqueID = temp.UniqueID, To = (int)EquipmentSlot.左戒指, Success = true });

            Enqueue(new S.RefreshItem { Item = Info.Inventory[index] });
            Enqueue(new S.RefreshItem { Item = Info.Equipment[(int)EquipmentSlot.左戒指] });

        }

        public void MarriageRequest()
        {

            if (Info.Married != 0)
            {
                ReceiveChat(string.Format("你已结婚"), ChatType.System);
                return;
            }

            if (Info.MarriedDate.AddDays(Settings.MarriageCooldown) > Envir.Now)
            {
                ReceiveChat(string.Format("不能结婚 离婚冷静期为：{0} 天", Settings.MarriageCooldown), ChatType.System);
                return;
            }

            if (Info.Level < Settings.MarriageLevelRequired)
            {
                ReceiveChat(string.Format("结婚要求等级：{0} 级", Settings.MarriageLevelRequired), ChatType.System);
                return;
            }

            Point target = Functions.PointMove(CurrentLocation, Direction, 1);

            if (!CurrentMap.ValidPoint(target)) return;
            Cell cell = CurrentMap.GetCell(target);
            PlayerObject player = null;

            if (cell.Objects == null || cell.Objects.Count < 1) return;

            for (int i = 0; i < cell.Objects.Count; i++)
            {
                MapObject ob = cell.Objects[i];
                if (ob.Race != ObjectType.Player) continue;

                player = Envir.GetPlayer(ob.Name);
            }



            if (player != null)
            {


                if (!Functions.FacingEachOther(Direction, CurrentLocation, player.Direction, player.CurrentLocation))
                {
                    ReceiveChat(string.Format("需要面对面才能完成结婚"), ChatType.System);
                    return;
                }

                if (player.Level < Settings.MarriageLevelRequired)
                {
                    ReceiveChat(string.Format("结婚对象要求等级：{0} 级", Settings.MarriageLevelRequired), ChatType.System);
                    return;
                }

                if (player.Info.MarriedDate.AddDays(Settings.MarriageCooldown) > Envir.Now)
                {
                    ReceiveChat(string.Format("{0} 不能结婚 离婚后有 {1} 天冷静期", player.Name, Settings.MarriageCooldown), ChatType.System);
                    return;
                }

                if (!player.AllowMarriage)
                {
                    ReceiveChat("对方拒绝了你的结婚请求", ChatType.System);
                    return;
                }

                if (player == this)
                {
                    ReceiveChat("不能跟自己结婚", ChatType.System);
                    return;
                }

                if (player.Dead || Dead)
                {
                    ReceiveChat("结婚对象角色死亡", ChatType.System);
                    return;
                }

                if (player.MarriageProposal != null)
                {
                    ReceiveChat(string.Format("{0} 已有结婚邀请", player.Info.Name), ChatType.System);
                    return;
                }

                if (!Functions.InRange(player.CurrentLocation, CurrentLocation, Globals.DataRange) || player.CurrentMap != CurrentMap)
                {
                    ReceiveChat(string.Format("{0} 非结婚范围内", player.Info.Name), ChatType.System);
                    return;
                }

                if (player.Info.Married != 0)
                {
                    ReceiveChat(string.Format("{0} 对方已婚", player.Info.Name), ChatType.System);
                    return;
                }

                player.MarriageProposal = this;
                player.Enqueue(new S.MarriageRequest { Name = Info.Name });
            }
            else
            {
                ReceiveChat(string.Format("暂不支持向同性求婚"), ChatType.System);
                return;
            }
        }

        public void MarriageReply(bool accept)
        {
            if (MarriageProposal == null || MarriageProposal.Info == null)
            {
                MarriageProposal = null;
                return;
            }

            if (!accept)
            {
                MarriageProposal.ReceiveChat(string.Format("{0} 拒绝求婚", Info.Name), ChatType.System);
                MarriageProposal = null;
                return;
            }

            if (Info.Married != 0)
            {
                ReceiveChat("你已结婚", ChatType.System);
                MarriageProposal = null;
                return;
            }

            if (MarriageProposal.Info.Married != 0)
            {
                ReceiveChat(string.Format("{0} 已婚", MarriageProposal.Info.Name), ChatType.System);
                MarriageProposal = null;
                return;
            }


            MarriageProposal.Info.Married = Info.Index;
            MarriageProposal.Info.MarriedDate = Envir.Now;

            Info.Married = MarriageProposal.Info.Index;
            Info.MarriedDate = Envir.Now;

            GetRelationship(false);
            MarriageProposal.GetRelationship(false);

            MarriageProposal.ReceiveChat(string.Format("恭喜！你现在迎娶了{0}", Info.Name), ChatType.System);
            ReceiveChat(String.Format("恭喜！你现在嫁给了{0}", MarriageProposal.Info.Name), ChatType.System);

            MarriageProposal = null;
        }

        public void DivorceRequest()
        {

            if (Info.Married == 0)
            {
                ReceiveChat(string.Format("你还没结婚"), ChatType.System);
                return;
            }


            Point target = Functions.PointMove(CurrentLocation, Direction, 1);
            if (!CurrentMap.ValidPoint(target)) return;
            Cell cell = CurrentMap.GetCell(target);
            PlayerObject player = null;

            if (cell.Objects == null || cell.Objects.Count < 1) return;

            for (int i = 0; i < cell.Objects.Count; i++)
            {
                MapObject ob = cell.Objects[i];
                if (ob.Race != ObjectType.Player) continue;

                player = Envir.GetPlayer(ob.Name);
            }

            if (player == null)
            {
                ReceiveChat(string.Format("必须面对面才能完成离婚"), ChatType.System);
                return;
            }

            if (player != null)
            {
                if (!Functions.FacingEachOther(Direction, CurrentLocation, player.Direction, player.CurrentLocation))
                {
                    ReceiveChat(string.Format("必须面对面才能完成离婚"), ChatType.System);
                    return;
                }

                if (player == this)
                {
                    ReceiveChat("不能自己离婚", ChatType.System);
                    return;
                }

                if (player.Dead || Dead)
                {
                    ReceiveChat("离婚对象角色死亡", ChatType.System);
                    return;
                }

                if (player.Info.Index != Info.Married)
                {
                    ReceiveChat(string.Format("你还没有嫁给{0}", player.Info.Name), ChatType.System);
                    return;
                }

                if (!Functions.InRange(player.CurrentLocation, CurrentLocation, Globals.DataRange) || player.CurrentMap != CurrentMap)
                {
                    ReceiveChat(string.Format("{0} 不在离婚范围内", player.Info.Name), ChatType.System);
                    return;
                }

                player.DivorceProposal = this;
                player.Enqueue(new S.DivorceRequest { Name = Info.Name });
            }
            else
            {
                ReceiveChat(string.Format("必须面对面才能完成离婚"), ChatType.System);
                return;
            }
        }

        public void DivorceReply(bool accept)
        {
            if (DivorceProposal == null || DivorceProposal.Info == null)
            {
                DivorceProposal = null;
                return;
            }

            if (!accept)
            {
                DivorceProposal.ReceiveChat(string.Format("{0} 拒绝和你离婚", Info.Name), ChatType.System);
                DivorceProposal = null;
                return;
            }

            if (Info.Married == 0)
            {
                ReceiveChat("未婚，所以不需要离婚", ChatType.System);
                DivorceProposal = null;
                return;
            }

            DivorceProposal.Info.Married = 0;
            DivorceProposal.Info.MarriedDate = Envir.Now;
            if (DivorceProposal.Info.Equipment[(int)EquipmentSlot.左戒指] != null)
            {
                DivorceProposal.Info.Equipment[(int)EquipmentSlot.左戒指].WeddingRing = -1;
                DivorceProposal.Enqueue(new S.RefreshItem { Item = DivorceProposal.Info.Equipment[(int)EquipmentSlot.左戒指] });
            }

            Info.Married = 0;
            Info.MarriedDate = Envir.Now;
            if (Info.Equipment[(int)EquipmentSlot.左戒指] != null)
            {
                Info.Equipment[(int)EquipmentSlot.左戒指].WeddingRing = -1;
                Enqueue(new S.RefreshItem { Item = Info.Equipment[(int)EquipmentSlot.左戒指] });
            }

            DivorceProposal.ReceiveChat(string.Format("你现在离婚了", Info.Name), ChatType.System);
            ReceiveChat("你已离婚了", ChatType.System);

            GetRelationship(false);
            DivorceProposal.GetRelationship(false);
            DivorceProposal = null;
        }

        public void GetRelationship(bool CheckOnline = true)
        {
            if (Info.Married == 0)
            {
                Enqueue(new S.LoverUpdate { Name = "", Date = Info.MarriedDate, MapName = "", MarriedDays = 0 });
            }
            else
            {
                CharacterInfo Lover = Envir.GetCharacterInfo(Info.Married);

                PlayerObject player = Envir.GetPlayer(Lover.Name);

                if (player == null)
                    Enqueue(new S.LoverUpdate { Name = Lover.Name, Date = Info.MarriedDate, MapName = "", MarriedDays = (short)(Envir.Now - Info.MarriedDate).TotalDays });
                else
                {
                    Enqueue(new S.LoverUpdate { Name = Lover.Name, Date = Info.MarriedDate, MapName = player.CurrentMap.Info.Title, MarriedDays = (short)(Envir.Now - Info.MarriedDate).TotalDays });
                    if (CheckOnline)
                    {
                        player.GetRelationship(false);
                        player.ReceiveChat(String.Format("{0} 上线了", Info.Name), ChatType.System);
                    }
                }
            }
        }
        public void LogoutRelationship()
        {
            if (Info.Married == 0) return;
            CharacterInfo lover = Envir.GetCharacterInfo(Info.Married);

            if (lover == null)
            {
                MessageQueue.EnqueueDebugging(Name + " 已结婚，但找不到结婚ID" + Info.Married);
                return;
            }

            PlayerObject player = Envir.GetPlayer(lover.Name);
            if (player != null)
            {
                player.Enqueue(new S.LoverUpdate { Name = Info.Name, Date = player.Info.MarriedDate, MapName = "", MarriedDays = (short)(Envir.Now - Info.MarriedDate).TotalDays });
                player.ReceiveChat(String.Format("{0} 已离线", Info.Name), ChatType.System);
            }
        }

        #endregion

        #region Mentorship

        public void MentorBreak(bool force = false)
        {
            if (Info.Mentor == 0)
            {
                ReceiveChat(GameLanguage.NoMentorship, ChatType.System);
                return;
            }

            CharacterInfo partner = Envir.GetCharacterInfo(Info.Mentor);
            PlayerObject partnerP = Envir.GetPlayer(partner.Name);

            if (force)
            {
                Info.MentorDate = Envir.Now.AddDays(Settings.MentorLength);
                ReceiveChat(String.Format("将有 {0} 天的延期才能使用师徒功能", Settings.MentorLength), ChatType.System);
            }
            else
            {
                ReceiveChat("师徒修炼完成！师徒关系自动解除", ChatType.System);
            }

            if (Info.IsMentor)
            {
                if (partnerP != null)
                {
                    Info.MentorExp += partnerP.MenteeEXP;
                    partnerP.MenteeEXP = 0;
                }
            }
            else
            {
                if (partnerP != null)
                {
                    partner.MentorExp += MenteeEXP;
                    MenteeEXP = 0;
                }
            }

            Info.Mentor = 0;
            GetMentor(false);
           
            if (Info.IsMentor && Info.MentorExp > 0)
            {
                GainExp((uint)Info.MentorExp);
                Info.MentorExp = 0;
            }
            
            partner.Mentor = 0;
            
            if (partnerP != null)
            {
                partnerP.ReceiveChat("师徒修炼完成！已自动解除师徒关系", ChatType.System);
                partnerP.GetMentor(false);
                if (partner.IsMentor && partner.MentorExp > 0)
                {
                    partnerP.GainExp((uint)partner.MentorExp);
                    Info.MentorExp = 0;
                }
            }
            else
            {
                if (partner.IsMentor && partner.MentorExp > 0)
                {
                    partner.Experience += partner.MentorExp;
                    partner.MentorExp = 0;
                }
            }

            Info.IsMentor = false;
            partner.IsMentor = false;
            Info.MentorExp = 0;
            partner.MentorExp = 0;
        }

        public void AddMentor(string Name)
        {
            if (Info.Mentor != 0)
            {
                ReceiveChat("已经有师傅", ChatType.System);
                return;
            }

            if (Info.Name == Name)
            {
                ReceiveChat("不能拜自己为师", ChatType.System);
                return;
            }

            if (Info.MentorDate > Envir.Now)
            {
                ReceiveChat("不能再收徒弟", ChatType.System);
                return;
            }

            PlayerObject mentor = Envir.GetPlayer(Name);

            if (mentor == null)
            {
                ReceiveChat(String.Format("未找到名字 {0}", Name), ChatType.System);
            }
            else
            {
                mentor.MentorRequest = null;

                if (!mentor.AllowMentor)
                {
                    ReceiveChat(String.Format("{0} 未开启拜师请求", mentor.Info.Name), ChatType.System);
                    return;
                }

                if (mentor.Info.MentorDate > Envir.Now)
                {
                    ReceiveChat(String.Format("{0} 不能再收徒弟了", mentor.Info.Name), ChatType.System);
                    return;
                }

                if (mentor.Info.Mentor != 0)
                {
                    ReceiveChat(String.Format("{0} 已经有徒弟了", mentor.Info.Name), ChatType.System);
                    return;
                }

                if (Info.Class != mentor.Info.Class)
                {
                    ReceiveChat("只能向同职业拜师", ChatType.System);
                    return;
                }
                if ((Info.Level + Settings.MentorLevelGap) > mentor.Level)
                {
                    ReceiveChat(String.Format("师傅必须比你高 {0} (级)", Settings.MentorLevelGap), ChatType.System);
                    return;
                }

                mentor.MentorRequest = this;
                mentor.Enqueue(new S.MentorRequest { Name = Info.Name, Level = Info.Level });
                ReceiveChat(String.Format("拜师请求以发送"), ChatType.System);
            }

        }

        public void MentorReply(bool accept)
        {
            if (MentorRequest == null || MentorRequest.Info == null)
            {
                MentorRequest = null;
                return;
            }

            if (!accept)
            {
                MentorRequest.ReceiveChat(string.Format("{0} 拒绝拜师请求", Info.Name), ChatType.System);
                MentorRequest = null;
                return;
            }

            if (Info.Mentor != 0)
            {
                ReceiveChat("已经有徒弟了", ChatType.System);
                return;
            }

            PlayerObject student = Envir.GetPlayer(MentorRequest.Info.Name);
            MentorRequest = null;

            if (student == null)
            {
                ReceiveChat(String.Format("{0} 不在线上", student.Name), ChatType.System);
                return;
            }
            else
            {
                if (student.Info.Mentor != 0)
                {
                    ReceiveChat(String.Format("{0} 已有师傅", student.Info.Name), ChatType.System);
                    return;
                }
                if (Info.Class != student.Info.Class)
                {
                    ReceiveChat("只能收同职业的玩家为徒", ChatType.System);
                    return;
                }
                if ((Info.Level - Settings.MentorLevelGap) < student.Level)
                {
                    ReceiveChat(String.Format("只能收低于自己 {0} 等级(s) 为徒弟", Settings.MentorLevelGap), ChatType.System);
                    return;
                }

                student.Info.Mentor = Info.Index;
                student.Info.IsMentor = false;
                Info.Mentor = student.Info.Index;
                Info.IsMentor = true;
                student.Info.MentorDate = Envir.Now;
                Info.MentorDate = Envir.Now;

                ReceiveChat(String.Format("收徒成功！ {0} 现在是你的徒弟", student.Info.Name), ChatType.System);
                student.ReceiveChat(String.Format("拜师成功！ {0} 现在是你的师傅", Info.Name), ChatType.System);
                GetMentor(false);
                student.GetMentor(false);
            }
        }

        public void GetMentor(bool CheckOnline = true)
        {
            if (Info.Mentor == 0)
            {
                Enqueue(new S.MentorUpdate { Name = "", Level = 0, Online = false, MenteeEXP = 0 });
            }
            else
            {
                CharacterInfo mentor = Envir.GetCharacterInfo(Info.Mentor);

                PlayerObject player = Envir.GetPlayer(mentor.Name);

                Enqueue(new S.MentorUpdate { Name = mentor.Name, Level = mentor.Level, Online = player != null, MenteeEXP = Info.MentorExp });

                if (player != null && CheckOnline)
                {
                    player.GetMentor(false);
                    player.ReceiveChat(String.Format("{0} 已经上线", Info.Name), ChatType.System);
                }
            }
        }

        public void LogoutMentor()
        {
            if (Info.Mentor == 0) return;

            CharacterInfo mentor = Envir.GetCharacterInfo(Info.Mentor);

            if (mentor == null)
            {
                MessageQueue.EnqueueDebugging(Name + " 虽然有师傅，但找不到师傅的ID " + Info.Mentor);
                return;
            }

            PlayerObject player = Envir.GetPlayer(mentor.Name);

            if (!Info.IsMentor)
            {
                mentor.MentorExp += MenteeEXP;
            }

            if (player != null)
            {
                player.Enqueue(new S.MentorUpdate { Name = Info.Name, Level = Info.Level, Online = false, MenteeEXP = mentor.MentorExp });
                player.ReceiveChat(String.Format("{0} 已经离线", Info.Name), ChatType.System);
            }
        }

        #endregion

        #region Gameshop

        public void GameShopStock(GameShopItem item)
        {
            int purchased;
            int StockLevel;

            if (item.iStock) //Invididual Stock
            {
                Info.GSpurchases.TryGetValue(item.Info.Index, out purchased);
            }
            else //Server Stock
            {
                Envir.GameshopLog.TryGetValue(item.Info.Index, out purchased);
            }

            if (item.Stock - purchased >= 0)
            {
                StockLevel = item.Stock - purchased;
                Enqueue(new S.GameShopStock { GIndex = item.Info.Index, StockLevel = StockLevel });
            }
              
        }

        public void GameshopBuy(int GIndex, byte Quantity, int PType)
        {
            if (Quantity < 1 || Quantity > 99) return;

            List<GameShopItem> shopList = Envir.GameShopList;
            GameShopItem Product = null;

            int purchased;
            bool stockAvailable = false;
            bool canAfford = false;
            uint CreditCost = 0;
            uint GoldCost = 0;

            List<UserItem> mailItems = new List<UserItem>();

            for (int i = 0; i < shopList.Count; i++)
            {
                if (shopList[i].GIndex == GIndex)
                {
                    Product = shopList[i];
                    break;
                }
            }

            if (Product == null)
            {
                ReceiveChat("购买的物品不在商店内", ChatType.System);
                MessageQueue.EnqueueDebugging(Info.Name + " 试图购买的物品不存在或以下架");
                return;
            }

            if (((decimal)(Quantity * Product.Count) / Product.Info.StackSize) > 5) return;

            if (Product.Stock != 0)
            {

                if (Product.iStock) //Invididual Stock
                {
                    Info.GSpurchases.TryGetValue(Product.Info.Index, out purchased);
                }
                else //Server Stock
                {
                    Envir.GameshopLog.TryGetValue(Product.Info.Index, out purchased);
                }

                if (Product.Stock - purchased - Quantity >= 0)
                {
                    stockAvailable = true;
                }
                else
                {
                    ReceiveChat("购买的商品数量超过了存货数量", ChatType.System);
                    GameShopStock(Product);
                    MessageQueue.EnqueueDebugging(Info.Name + " 正在尝试购买 " + Product.Info.FriendlyName + " x " + Quantity + " - Stock isn't available.");
                    return;
                }
            }
            else
            {
                stockAvailable = true;
            }

            if (stockAvailable)
            {
                MessageQueue.EnqueueDebugging(Info.Name + " 正在尝试购买 " + Product.Info.FriendlyName + " x " + Quantity + " - 仓库可用");

                if (PType == 0)
                {
                    var cost = Product.CreditPrice * Quantity;
                    if (Product.CanBuyCredit && cost <= Account.Credit)
                    {
                        canAfford = true;
                        CreditCost = cost;
                    }
                }
                else if (PType == 1)
                {
                    var goldcost = Product.GoldPrice * Quantity;
                    if (Product.CanBuyGold && goldcost <= Account.Gold)
                    {
                        canAfford = true;
                        GoldCost = goldcost;
                    }
                }
                else
                {
                    ReceiveChat("没有足够的实力购买", ChatType.System);
                    MessageQueue.EnqueueDebugging(Info.Name + " 正在尝试购买 " + Product.Info.FriendlyName + " x " + Quantity + " - 没有足够的货币");
                    return;
                }
            }
            else
            {
                return;
            }

            if (canAfford)
            {
                MessageQueue.EnqueueDebugging(Info.Name + " 正在尝试购买 " + Product.Info.FriendlyName + " x " + Quantity + " - 货币充足");
                if (PType == 0)
                {
                    Account.Credit -= CreditCost;
                    Report.CreditChanged(CreditCost, true, Product.Info.FriendlyName);
                    if (CreditCost != 0) Enqueue(new S.LoseCredit { Credit = CreditCost });

                }
                if (PType == 1)
                {
                    Account.Gold -= GoldCost;
                    Report.GoldChanged(GoldCost, true, Product.Info.FriendlyName);
                    if (GoldCost != 0) Enqueue(new S.LoseGold { Gold = GoldCost });
                }

                if (Product.iStock && Product.Stock != 0)
                {
                    Info.GSpurchases.TryGetValue(Product.Info.Index, out purchased);
                    if (purchased == 0)
                    {
                        Info.GSpurchases[Product.GIndex] = Quantity;
                    }
                    else
                    {
                        Info.GSpurchases[Product.GIndex] += Quantity;
                    }
                }

                Envir.GameshopLog.TryGetValue(Product.Info.Index, out purchased);
                if (purchased == 0)
                {
                    Envir.GameshopLog[Product.GIndex] = Quantity;
                }
                else
                {
                    Envir.GameshopLog[Product.GIndex] += Quantity;
                }

                if (Product.Stock != 0) GameShopStock(Product);
            }
            else
            {
                return;
            }

            Report.ItemGSBought(Product, Quantity, CreditCost, GoldCost);

            ushort quantity = (ushort)(Quantity * Product.Count);

            if (Product.Info.StackSize <= 1 || quantity == 1)
            {
                for (int i = 0; i < Quantity; i++)
                {
                    UserItem mailItem = Envir.CreateFreshItem(Envir.GetItemInfo(Product.Info.Index));

                    mailItems.Add(mailItem);
                }
            }
            else
            {
                while (quantity > 0)
                {
                    UserItem mailItem = Envir.CreateFreshItem(Envir.GetItemInfo(Product.Info.Index));
                    mailItem.Count = 0;
                    for (int i = 0; i < mailItem.Info.StackSize; i++)
                    {
                        mailItem.Count++;
                        quantity--;
                        if (quantity == 0) break;
                    }
                    if (mailItem.Count == 0) break;

                    mailItems.Add(mailItem);

                }
            }

            MailInfo mail = new MailInfo(Info.Index)
            {
                MailID = ++Envir.NextMailID,
                Sender = "游戏商城",
                Message = "感谢您从游戏商店购物，随函附上所购买的商品",
                Items = mailItems,
            };
            mail.Send();

            MessageQueue.EnqueueDebugging(Info.Name + " 正在尝试购买 " + Product.Info.FriendlyName + " x " + Quantity + " - 购买已发送");
            ReceiveChat("购买的商品已发送到您的邮箱", ChatType.Hint);
        }

        public void GetGameShop()
        {
            int purchased;
            int stockLevel;

            for (int i = 0; i < Envir.GameShopList.Count; i++)
            {
                var item = Envir.GameShopList[i];

                if (item.Stock != 0)
                {
                    if (item.iStock) //Individual Stock
                    {
                        Info.GSpurchases.TryGetValue(item.Info.Index, out purchased);
                    }
                    else //Server Stock
                    {
                        Envir.GameshopLog.TryGetValue(item.Info.Index, out purchased);
                    }

                    if (item.Stock - purchased >= 0)
                    {
                        stockLevel = item.Stock - purchased;
                        Enqueue(new S.GameShopInfo { Item = item, StockLevel = stockLevel });
                    }
                }
                else
                {
                    Enqueue(new S.GameShopInfo { Item = item, StockLevel = item.Stock });
                }  
            }
        }

        #endregion

        #region ConquestWall
        public void CheckConquest(bool checkPalace = false)
        {
            if (CurrentMap.tempConquest == null && CurrentMap.Conquest != null)
            {
                ConquestObject swi = CurrentMap.GetConquest(CurrentLocation);
                if (swi != null)
                    EnterSabuk();
                else
                    LeaveSabuk();
            }
            else if (CurrentMap.tempConquest != null)
            {
                if (checkPalace && CurrentMap.Info.Index == CurrentMap.tempConquest.PalaceMap.Info.Index && CurrentMap.tempConquest.GameType == ConquestGame.占领皇宫)
                    CurrentMap.tempConquest.TakeConquest(this);

                EnterSabuk();
            }
        }
        public void EnterSabuk()
        {
            if (WarZone) return;
            WarZone = true;
            RefreshNameColour();
        }

        public void LeaveSabuk()
        {
            if (!WarZone) return;
            WarZone = false;
            RefreshNameColour();
        }
        #endregion

        #region Rental

        public void GetRentedItems()
        {
            Enqueue(new S.GetRentedItems { RentedItems = Info.RentedItems });
        }

        public void ItemRentalRequest()
        {
            if (Dead)
            {
                ReceiveChat("死亡时无法租用物品", ChatType.System);
                return;
            }

            if (ItemRentalPartner != null)
            {
                ReceiveChat("已经将物品出租给其他玩家", ChatType.System);
                return;
            }

            var targetPosition = Functions.PointMove(CurrentLocation, Direction, 1);
            if (!CurrentMap.ValidPoint(targetPosition)) return;
            var targetCell = CurrentMap.GetCell(targetPosition);
            PlayerObject targetPlayer = null;

            if (targetCell.Objects == null || targetCell.Objects.Count < 1)
                return;

            foreach (var mapObject in targetCell.Objects)
            {
                if (mapObject.Race != ObjectType.Player)
                    continue;

                targetPlayer = Envir.GetPlayer(mapObject.Name);
            }

            if (targetPlayer == null)
            {
                ReceiveChat("面向你想租借物品的玩家", ChatType.System);
                return;
            }

            if (Info.RentedItems.Count >= 3)
            {
                ReceiveChat("一次不能租用超过3件物品", ChatType.System);
                return;
            }

            if (targetPlayer.Info.HasRentedItem)
            {
                ReceiveChat($"{targetPlayer.Name} 目前无法再租用物品", ChatType.System);
                return;
            }

            if (!Functions.FacingEachOther(Direction, CurrentLocation, targetPlayer.Direction,
                targetPlayer.CurrentLocation))
            {
                ReceiveChat("面对你想租借物品的玩家", ChatType.System);
                return;
            }

            if (targetPlayer == this)
            {
                ReceiveChat("无法将物品出租给自己", ChatType.System);
                return;
            }

            if (targetPlayer.Dead)
            {
                ReceiveChat($"死后无法将物品出租给 {targetPlayer.Name}", ChatType.System);
                return;
            }

            if (!Functions.InRange(targetPlayer.CurrentLocation, CurrentLocation, Globals.DataRange)
                || targetPlayer.CurrentMap != CurrentMap)
            {
                ReceiveChat($"{targetPlayer.Name} 不在范围内", ChatType.System);
                return;
            }

            if (targetPlayer.ItemRentalPartner != null)
            {
                ReceiveChat($"{targetPlayer.Name} 当前正忙，请稍后再试", ChatType.System);
                return;
            }

            ItemRentalPartner = targetPlayer;
            targetPlayer.ItemRentalPartner = this;

            Enqueue(new S.ItemRentalRequest { Name = targetPlayer.Name, Renting = false });
            ItemRentalPartner.Enqueue(new S.ItemRentalRequest { Name = Name, Renting = true });
        }

        public void SetItemRentalFee(uint amount)
        {
            if (ItemRentalFeeLocked)
                return;

            if ((ulong)amount + ItemRentalFeeAmount >= uint.MaxValue)
                return;

            if (Account.Gold < amount)
                return;

            if (ItemRentalPartner == null)
                return;

            ItemRentalFeeAmount += amount;
            Account.Gold -= amount;

            Enqueue(new S.LoseGold { Gold = amount });
            ItemRentalPartner.Enqueue(new S.ItemRentalFee { Amount = amount });
        }

        public void SetItemRentalPeriodLength(uint days)
        {
            if (days < 1 || days > 30)
                return;

            if (ItemRentalItemLocked)
                return;

            if (ItemRentalPartner == null)
                return;

            ItemRentalPeriodLength = days;
            ItemRentalPartner.Enqueue(new S.ItemRentalPeriod { Days = days });
        }

        public void DepositRentalItem(int from, int to)
        {
            var packet = new S.DepositRentalItem { From = from, To = to, Success = false };

            if (ItemRentalItemLocked)
            {
                Enqueue(packet);
                return;
            }

            if (from < 0 || from >= Info.Inventory.Length)
            {
                Enqueue(packet);
                return;
            }

            // TODO: Change this check.
            if (to < 0 || to >= 1)
            {
                Enqueue(packet);
                return;
            }

            var item = Info.Inventory[from];

            if (item == null)
            {
                Enqueue(packet);
                return;
            }

            if (item.RentalInformation?.RentalLocked == true)
            {
                ReceiveChat($"无法出租 {item.FriendlyName} 直到 {item.RentalInformation.ExpiryDate}", ChatType.System);
                Enqueue(packet);
                return;
            }

            if (item.Info.Bind.HasFlag(BindMode.UnableToRent))
            {
                ReceiveChat($"无法出租 {item.FriendlyName}", ChatType.System);
                Enqueue(packet);
                return;
            }

            if (item.RentalInformation != null && item.RentalInformation.BindingFlags.HasFlag(BindMode.UnableToRent))
            {
                ReceiveChat($"无法出租 {item.FriendlyName} 因为它属于 {item.RentalInformation.OwnerName}", ChatType.System);
                Enqueue(packet);
                return;
            }

            if (ItemRentalDepositedItem == null)
            {
                ItemRentalDepositedItem = item;
                Info.Inventory[from] = null;

                packet.Success = true;
                RefreshBagWeight();
                UpdateRentalItem();
                Report.ItemMoved(item, MirGridType.Inventory, MirGridType.Renting, from, to);
            }

            Enqueue(packet);
        }

        public void RetrieveRentalItem(int from, int to)
        {
            var packet = new S.RetrieveRentalItem { From = from, To = to, Success = false };

            // TODO: Change this check.
            if (from < 0 || from >= 1)
            {
                Enqueue(packet);
                return;
            }

            if (to < 0 || to >= Info.Inventory.Length)
            {
                Enqueue(packet);
                return;
            }

            var item = ItemRentalDepositedItem;

            if (item == null)
            {
                Enqueue(packet);
                return;
            }

            if (Info.Inventory[to] == null)
            {
                Info.Inventory[to] = item;
                ItemRentalDepositedItem = null;

                packet.Success = true;
                RefreshBagWeight();
                UpdateRentalItem();
                Report.ItemMoved(item, MirGridType.Renting, MirGridType.Inventory, from, to);
            }

            Enqueue(packet);
        }

        private void UpdateRentalItem()
        {
            if (ItemRentalPartner == null)
                return;

            if (ItemRentalDepositedItem != null)
                ItemRentalPartner.CheckItem(ItemRentalDepositedItem);

            ItemRentalPartner.Enqueue(new S.UpdateRentalItem { LoanItem = ItemRentalDepositedItem });
        }

        public void CancelItemRental()
        {
            if (ItemRentalPartner == null)
                return;

            ItemRentalRemoveLocks();

            var rentalPair = new []  {
                ItemRentalPartner,
                this
            };

            for (var i = 0; i < 2; i++)
            {
                if (rentalPair[i] == null)
                    continue;

                if (rentalPair[i].ItemRentalDepositedItem != null)
                {
                    var item = rentalPair[i].ItemRentalDepositedItem;

                    if (FreeSpace(rentalPair[i].Info.Inventory) < 1)
                    {
                        rentalPair[i].GainItemMail(item, 1);
                        rentalPair[i].Enqueue(new S.DeleteItem { UniqueID = item.UniqueID, Count = item.Count });
                        rentalPair[i].ItemRentalDepositedItem = null;

                        Report.ItemMailed(item, item.Count, 1);

                        continue;
                    }

                    for (var j = 0; j < rentalPair[i].Info.Inventory.Length; j++)
                    {
                        if (rentalPair[i].Info.Inventory[j] != null)
                            continue;

                        if (rentalPair[i].CanGainItem(item))
                            rentalPair[i].RetrieveRentalItem(0, j);
                        else
                        {
                            rentalPair[i].GainItemMail(item, 1);
                            rentalPair[i].Enqueue(new S.DeleteItem { UniqueID = item.UniqueID, Count = item.Count });

                            Report.ItemMailed(item, item.Count, 1);
                        }

                        rentalPair[i].ItemRentalDepositedItem = null;

                        break;
                    }
                }
 
                if (rentalPair[i].ItemRentalFeeAmount > 0)
                {
                    rentalPair[i].GainGold(rentalPair[i].ItemRentalFeeAmount);
                    rentalPair[i].ItemRentalFeeAmount = 0;

                    Report.GoldChanged(rentalPair[i].ItemRentalFeeAmount, false);
                }

                rentalPair[i].ItemRentalPartner = null;
                rentalPair[i].Enqueue(new S.CancelItemRental());
            }
        }

        public void ItemRentalLockFee()
        {
            S.ItemRentalLock p = new S.ItemRentalLock { Success = false, GoldLocked = false, ItemLocked = false };

            if (ItemRentalFeeAmount > 0)
            {
                ItemRentalFeeLocked = true;
                p.GoldLocked = true;
                p.Success = true;

                ItemRentalPartner.Enqueue(new S.ItemRentalPartnerLock { GoldLocked = ItemRentalFeeLocked });
            }

            if (ItemRentalFeeLocked && ItemRentalPartner.ItemRentalItemLocked)
                ItemRentalPartner.Enqueue(new S.CanConfirmItemRental());
            else if (ItemRentalFeeLocked && !ItemRentalPartner.ItemRentalItemLocked)
                ItemRentalPartner.ReceiveChat($"{Name} 已经锁定了租金", ChatType.System);

            Enqueue(p);
        }

        public void ItemRentalLockItem()
        {
            S.ItemRentalLock p = new S.ItemRentalLock { Success = false, GoldLocked = false, ItemLocked = false };

            if (ItemRentalDepositedItem != null)
            {
                ItemRentalItemLocked = true;
                p.ItemLocked = true;
                p.Success = true;

                ItemRentalPartner.Enqueue(new S.ItemRentalPartnerLock { ItemLocked = ItemRentalItemLocked });
            }

            if (ItemRentalItemLocked && ItemRentalPartner.ItemRentalFeeLocked)
                Enqueue(new S.CanConfirmItemRental());
            else if (ItemRentalItemLocked && !ItemRentalPartner.ItemRentalFeeLocked)
                ItemRentalPartner.ReceiveChat($"{Name} 已锁定租赁物品", ChatType.System);


            Enqueue(p);
        }

        private void ItemRentalRemoveLocks()
        {
            ItemRentalFeeLocked = false;
            ItemRentalItemLocked = false;

            if (ItemRentalPartner == null)
                return;

            ItemRentalPartner.ItemRentalFeeLocked = false;
            ItemRentalPartner.ItemRentalItemLocked = false;
        }

        public void ConfirmItemRental()
        {
            if (ItemRentalPartner == null)
            {
                CancelItemRental();
                return;
            }

            if (Info.RentedItems.Count >= 3)
            {
                CancelItemRental();
                return;
            }

            if (ItemRentalPartner.Info.HasRentedItem)
            {
                CancelItemRental();
                return;
            }

            if (ItemRentalDepositedItem == null)
                return;

            if (ItemRentalPartner.ItemRentalFeeAmount <= 0)
                return;

            if (ItemRentalDepositedItem.Info.Bind.HasFlag(BindMode.UnableToRent))
                return;

            if (ItemRentalDepositedItem.RentalInformation != null &&
                ItemRentalDepositedItem.RentalInformation.BindingFlags.HasFlag(BindMode.UnableToRent))
                return;

            if (!Functions.InRange(ItemRentalPartner.CurrentLocation, CurrentLocation, Globals.DataRange)
                || ItemRentalPartner.CurrentMap != CurrentMap || !Functions.FacingEachOther(Direction, CurrentLocation,
                    ItemRentalPartner.Direction, ItemRentalPartner.CurrentLocation))
            {
                CancelItemRental();
                return;
            }

            if (!ItemRentalItemLocked && !ItemRentalPartner.ItemRentalFeeLocked)
                return;

            if (!ItemRentalPartner.CanGainItem(ItemRentalDepositedItem))
            {
                ReceiveChat($"{ItemRentalPartner.Name} 无法接收该物品", ChatType.System);
                Enqueue(new S.CancelItemRental());

                ItemRentalPartner.ReceiveChat("无法接收这个租赁物品", ChatType.System);
                ItemRentalPartner.Enqueue(new S.CancelItemRental());

                return;
            }

            if (!CanGainGold(ItemRentalPartner.ItemRentalFeeAmount))
            {
                ReceiveChat("不能再持有更多的金币", ChatType.System);
                Enqueue(new S.CancelItemRental());

                ItemRentalPartner.ReceiveChat($"{Name} 无法再接收更多的金币", ChatType.System);
                ItemRentalPartner.Enqueue(new S.CancelItemRental());

                return;
            }

            var item = ItemRentalDepositedItem;
            item.RentalInformation = new RentalInformation
            {
                OwnerName = Name,
                ExpiryDate = Envir.Now.AddDays(ItemRentalPeriodLength),
                BindingFlags = BindMode.DontDrop | BindMode.DontStore | BindMode.DontSell | BindMode.DontTrade | BindMode.UnableToRent | BindMode.DontUpgrade | BindMode.UnableToDisassemble
            };

            var itemRentalInformation = new ItemRentalInformation
            {
                ItemId = item.UniqueID,
                ItemName = item.FriendlyName,
                RentingPlayerName = ItemRentalPartner.Name,
                ItemReturnDate = item.RentalInformation.ExpiryDate,
                
            };

            Info.RentedItems.Add(itemRentalInformation);
            ItemRentalDepositedItem = null;

            ItemRentalPartner.GainItem(item);
            ItemRentalPartner.Info.HasRentedItem = true;
            ItemRentalPartner.ReceiveChat($"你已经租赁了 {item.FriendlyName} 从 {Name} 到 {item.RentalInformation.ExpiryDate}", ChatType.System);

            GainGold(ItemRentalPartner.ItemRentalFeeAmount);
            ReceiveChat($"收到 {ItemRentalPartner.ItemRentalFeeAmount} 金币用于物品租赁", ChatType.System);
            ItemRentalPartner.ItemRentalFeeAmount = 0;

            Enqueue(new S.ConfirmItemRental());
            ItemRentalPartner.Enqueue(new S.ConfirmItemRental());

            ItemRentalRemoveLocks();

            ItemRentalPartner.ItemRentalPartner = null;
            ItemRentalPartner = null;
        }

        #endregion

        public Server.MirEnvir.Timer GetTimer(string key)
        {
            var timerKey = Name + "-" + key;

            if (Envir.Timers.ContainsKey(timerKey))
            {
                return Envir.Timers[timerKey];
            }

            return null;
        }        
        public void SetTimer(string key, int seconds, byte type = 0)
        {
            if (seconds < 0) seconds = 0;

            var timerKey = Name + "-" + key;

            Timer t = new Timer(timerKey, seconds, type);

            Envir.Timers[timerKey] = t;

            Enqueue(new S.SetTimer { Key = t.Key, Seconds = t.Seconds, Type = t.Type });
        }
        public void ExpireTimer(string key)
        {
            var timerKey = Name + "-" + key;

            if (Envir.Timers.ContainsKey(timerKey))
            {
                Envir.Timers.Remove(timerKey);
            }

            Enqueue(new S.ExpireTimer { Key = timerKey });
        }
        public void SetCompass(Point location)
        {
            Enqueue(new S.SetCompass { Location = location });
        }
        public bool HasHero
        {
            get { return CurrentHero != null; }
        }
        public bool HeroSpawned
        {
            get { return Hero != null; }
        }
        public void SummonHero()
        {
            HeroObject hero = CurrentHero.Class switch
            {
                MirClass.战士 => new WarriorHero(CurrentHero, this),
                MirClass.法师 => new WizardHero(CurrentHero, this),
                MirClass.道士 => new TaoistHero(CurrentHero, this),
                MirClass.刺客 => new AssassinHero(CurrentHero, this),
                MirClass.弓箭 => new ArcherHero(CurrentHero, this),
                _ => new HeroObject(CurrentHero, this)
            };            

            hero.ActionTime = Envir.Time + 1000;
            hero.RefreshNameColour();

            if (!hero.Dead)
                SpawnHero(hero);

            Hero = hero;
            Info.HeroSpawned = true;
            Enqueue(new S.UpdateHeroSpawnState { State = hero.Dead ? HeroSpawnState.Dead : HeroSpawnState.Summoned });
        }
        private void SpawnHero(HeroObject hero)
        {
            if (CurrentMap.ValidPoint(Front))
                hero.Spawn(CurrentMap, Front);
            else
                hero.Spawn(CurrentMap, CurrentLocation);

            for (int i = 0; i < Buffs.Count; i++)
            {
                var buff = Buffs[i];
                buff.LastTime = Envir.Time;
                buff.ObjectID = ObjectID;

                AddBuff(buff.Type, null, (int)buff.ExpireTime, buff.Stats, true, true, buff.Values);
            }
        }
        public void DespawnHero()
        {         
            Hero.Despawn(true);
            Hero = null;
            Enqueue(new S.UpdateHeroSpawnState { State = HeroSpawnState.Unsummoned });
        }
        public void ReviveHero()
        {
            if (CurrentHero == null) return;
            if (CurrentHero.HP != 0) return;

            if (Hero != null)
            {
                if (Hero.Node != null)
                {
                    Hero.Revive(Hero.Stats[Stat.HP], true);
                }
                else
                {
                    CurrentHero.HP = Hero.Stats[Stat.HP];
                    Hero.Dead = false;
                    SpawnHero(Hero);
                }
                Enqueue(new S.UpdateHeroSpawnState { State = HeroSpawnState.Summoned });
            }
            else CurrentHero.HP = -1;
        }

        public void SealHero()
        {
            if (CurrentHero == null) return;
            if (FreeSpace(Info.Inventory) == 0) return;
            if (Settings.HeroSealItemName == string.Empty) return;

            if (Settings.HeroMaximumSealCount > 0 && CurrentHero.SealCount >= Settings.HeroMaximumSealCount)
            {
                ReceiveChat(string.Format("英雄不再被封印"), ChatType.Hint);
                return;
            }

            ItemInfo itemInfo = Envir.GetItemInfo(Settings.HeroSealItemName);
            if (itemInfo == null) return;

            if (Hero != null)
            {
                DespawnHero();
                Info.HeroSpawned = false;
                Enqueue(new S.UpdateHeroSpawnState { State = HeroSpawnState.None });
            }

            UserItem item = Envir.CreateFreshItem(itemInfo);            
            item.AddedStats[Stat.Hero] = CurrentHero.Index;
            if (CanGainItem(item))
                GainItem(item);

            CurrentHero.SealCount++;
            Info.Heroes[CurrentHeroIndex] = null;
            CurrentHero = null;
        }

        public void DeleteHero()
        {
            if (CurrentHero == null) return;

            if (Hero != null)
            {
                DespawnHero();
                Info.HeroSpawned = false;
                Enqueue(new S.UpdateHeroSpawnState { State = HeroSpawnState.None });
            }

            Info.Heroes[CurrentHeroIndex] = null;
            CurrentHero = null;
            ReceiveChat(string.Format("英雄已从游戏中删除"), ChatType.Hint);
        }

        private bool AddHero(HeroInfo hero)
        {
            int heroCount = Info.Heroes.Count(x => x != null);

            if (heroCount >= Info.MaximumHeroCount)
            {
                ReceiveChat(string.Format("无法再召唤新的英雄"), ChatType.Hint);
                return false;
            }

            for (int i = 0; i < Info.Heroes.Length; i++)
            {
                if (Info.Heroes[i] != null) continue;

                Info.Heroes[i] = hero;
                if (!HasHero)
                {
                    CurrentHero = hero;
                    SummonHero();
                }
                else
                {
                    ReceiveChat(string.Format("已添加到英雄存储库中"), ChatType.Hint);
                    Enqueue(new S.NewHeroInfo { Info = hero.ClientInformation, StorageIndex = i - 1 });
                }

                return true;
            }

            return false;
        }

        public void ManageHeroes()
        {
            S.ManageHeroes p = new S.ManageHeroes() { MaximumCount = Info.MaximumHeroCount, CurrentHero = CurrentHero?.ClientInformation };

            if (!Connection.HeroStorageSent)
            {
                p.Heroes = new ClientHeroInformation[Info.Heroes.Length - 1];
                for (int i = 1; i < Info.Heroes.Length; i++)
                    p.Heroes[i - 1] = Info.Heroes[i]?.ClientInformation;
                Connection.HeroStorageSent = true;
            }

            Enqueue(p);
        }

        public void SendNPCGoods(List<UserItem> goods, float rate, PanelType panelType, bool hideAddedStats = false)
        {
            Enqueue(new S.NPCGoods { List = goods, Rate = rate, Type = panelType, HideAddedStats = hideAddedStats });
        }
    }
}

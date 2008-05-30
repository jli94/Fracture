﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Task;

namespace MUDServer {
    public class Player : CombatEntity {
        public TelnetClient Client;
        private bool _LastPrompt;
        private int _NumMessagesSent;
        private delegate IEnumerator<object> CommandHandler (Player p, string[] words);
        private AlphaTrie<CommandHandler> _Commands;

        public Player (TelnetClient client, Location location)
            : base(location, null) {
            Client = client;
            client.RegisterOnDispose(OnDisconnected);

            RebuildCommandTrie();

            AddEventHandler(EventType.CombatHit, OnEventCombatHit);
            AddEventHandler(EventType.CombatMiss, OnEventCombatMiss);
            AddEventHandler(EventType.CombatStart, OnEventCombatStart);
            AddEventHandler(EventType.Death, OnEventDeath);
            AddEventHandler(EventType.Emote, OnEventEmote);
            AddEventHandler(EventType.Enter, OnEventEnter);
            AddEventHandler(EventType.Leave, OnEventLeave);
            AddEventHandler(EventType.Say, OnEventSay);
            AddEventHandler(EventType.Tell, OnEventTell);
        }

        private void RebuildCommandTrie () {
            _Commands = new AlphaTrie<CommandHandler>();
            AddPlayerCommands(_Commands);
        }

        public override void Dispose () {
            base.Dispose();
            World.Players.Remove(this.Name.ToLower());
            Console.WriteLine("{0} has left the world", Name);
        }

        private void OnDisconnected (Future f) {
            Location = null;
            this.Dispose();
        }

        public void SendMessage (string message, params object[] args) {
            _NumMessagesSent += 1;
            StringBuilder output = new StringBuilder();
            if (_LastPrompt) {
                output.AppendLine();
                _LastPrompt = false;
            }
            output.AppendFormat(message, args);
            output.AppendLine();
            Client.SendText(output.ToString());
        }

        public void SendPrompt () {
            _LastPrompt = true;
            Client.SendText(String.Format("{0}/{1}hp> ", CurrentHealth, MaximumHealth));
        }

        private void PerformLook () {
            if (Location.Description != null)
                SendMessage(Location.Description);
            if (Location.Exits.Count != 0) {
                SendMessage("Exits from this location:");
                for (int i = 0; i < Location.Exits.Count; i++) {
                    SendMessage("{0}: {1}", i, Location.Exits[i].Description);
                }
            }
            foreach (var e in this.Location.Entities) {
                if (e.Value != this)
                    SendMessage("{0} is {1}.", e.Value.Description, e.Value.State ?? "standing nearby");
            }
        }

        private static void AddPlayerCommands(AlphaTrie<CommandHandler> _) {
            _.Insert(
                "say",
                delegate(Player p, string[] words) {
                    if (words.Length < 2) {
                        p.SendMessage("What did you want to <say>, exactly?");
                        p.SendPrompt();
                    }
                    else {
                        Event.Send(new { Type = EventType.Say, Sender = p, Text = string.Join(" ", words, 1, words.Length - 1) });
                    }
                    return null;
                });

            _.Insert(
                "emote",
                delegate(Player p, string[] words) {
                    if (words.Length < 2) {
                        p.SendMessage("What were you trying to do?");
                        p.SendPrompt();
                    }
                    else {
                        Event.Send(new { Type = EventType.Emote, Sender = p, Text = string.Join(" ", words, 1, words.Length - 1) });
                    }
                    return null;
                });

            _.Insert(
                "tell",
                delegate(Player p, string[] words) {
                    if (words.Length < 3) {
                        p.SendMessage("Who did you want to <tell> what?");
                        p.SendPrompt();
                    }
                    else {
                        string name = words[1];
                        IEntity to = p.Location.ResolveName(name);
                        if (to != null) {
                            Event.Send(new { Type = EventType.Tell, Sender = p, Recipient = to, Text = string.Join(" ", words, 2, words.Length - 2) });
                        }
                        else {
                            p.SendMessage("Who do you think you're talking to? There's nobody named {0} here.", name);
                            p.SendPrompt();
                        }
                    }
                    return null;
                });

            _.Insert(
                "look",
                delegate(Player p, string[] words) {
                    p.PerformLook();
                    p.SendPrompt();
                    return null;
                });

            _.Insert(
                "go",
                delegate(Player p, string[] words) {
                    if (p.CurrentHealth <= 0) {
                        p.SendMessage("You can't do that while dead.");
                        p.SendPrompt();
                        return null;
                    }
                    try {
                        int exitId;
                        string exitText = string.Join(" ", words, 1, words.Length - 1).Trim().ToLower();
                        Action<Exit> go = (exit) => {
                            if (World.Locations.ContainsKey(exit.Target.ToLower()))
                                p.Location = World.Locations[exit.Target.ToLower()];
                            else {
                                Console.WriteLine("Warning: '{0}' exit '{1}' leads to undefined location '{2}'.", p.Location.Name, exit.Description, exit.Target);
                                p.SendMessage("Your attempt to leave via {0} is thwarted by a mysterious force.", exit.Description);
                                p.SendPrompt();
                            }
                        };
                        if (int.TryParse(exitText, out exitId)) {
                            go(p.Location.Exits[exitId]);
                        }
                        else {
                            foreach (var e in p.Location.Exits) {
                                if (e.Description.ToLower().Contains(exitText)) {
                                    go(e);
                                    break;
                                }
                            }
                        }
                    }
                    catch {
                        p.SendMessage("You can't find that exit.");
                        p.SendPrompt();
                    }
                    return null;
                });

            _.Insert(
                "kill",
                delegate(Player p, string[] words) {
                    if (words.Length < 2) {
                        p.SendMessage("Who did you want to kill?");
                        p.SendPrompt();
                    }
                    else if (p.CurrentHealth <= 0) {
                        p.SendMessage("You can't do that while dead.");
                        p.SendPrompt();
                    }
                    else if (p.InCombat) {
                        p.SendMessage("You're already busy fighting!");
                        p.SendPrompt();
                    }
                    else {
                        string name = words[1];
                        IEntity to = p.Location.ResolveName(name);
                        if (to == p) {
                            p.SendMessage("You don't really want to kill yourself, you're just looking for attention.");
                            p.SendPrompt();
                        }
                        else if (to != null) {
                            if (to is CombatEntity) {
                                CombatEntity cto = to as CombatEntity;
                                if (cto.InCombat == false) {
                                    p.StartCombat(cto);
                                    cto.StartCombat(p);
                                    Event.Send(new { Type = EventType.CombatStart, Sender = p, Target = cto });
                                }
                                else {
                                    p.SendMessage("They're already in combat, and you don't want to interfere.");
                                    p.SendPrompt();
                                }
                            }
                            else {
                                p.SendMessage("You don't think that's such a great idea.");
                                p.SendPrompt();
                            }
                        }
                        else {
                            p.SendMessage("Who are you trying to kill, exactly? There's nobody named {0} here.", name);
                            p.SendPrompt();
                        }
                    }
                    return null;
                });

            _.Insert(
                "help",
                delegate(Player p, string[] words) {
                    p.SendMessage("You can <say> things to those nearby, if you feel like chatting.");
                    p.SendMessage("You can also <tell> somebody things if you wish to speak privately.");
                    p.SendMessage("You can also <emote> within sight of others.");
                    p.SendMessage("If you're feeling lost, try taking a <look> around.");
                    p.SendMessage("If you wish to <go> out an exit, simply speak its name or number.");
                    p.SendMessage("Looking to make trouble? Try to <kill> someone!");
                    p.SendPrompt();
                    return null;
                });

        }

        public IEnumerator<object> ProcessInput (string text) {
            string[] words = text.Split(' ');
            if (words.Length < 1)
                return null;
            string firstWord = words[0].ToLower();

            _LastPrompt = false;

            var cmd = _Commands.FindByKeyStart(firstWord);
            if (cmd == null) {
                SendMessage("Hmm... that doesn't make any sense. Do you need some <help>?");
                SendPrompt();
                return null;
            }
            return cmd.Value.Invoke(this, words);
        }

        private IEnumerator<object> PromptHelper (IEnumerator<object> task, OnComplete onComplete) {
            Start ts = new Start(task, TaskExecutionPolicy.RunAsBackgroundTask);
            yield return ts;
            ts.Future.RegisterOnComplete(onComplete);
        }

        // Ensure that we send a new prompt to the user after dispatching any events that send output
        protected override IEnumerator<object> DispatchEvent (EventType type, object evt) {
            int prevMessages = _NumMessagesSent;
            OnComplete oc = (f, r, e) => {
                if (_NumMessagesSent > prevMessages)
                    SendPrompt();
            };

            IEnumerator<object> result = base.DispatchEvent(type, evt);

            if (result != null) {
                return PromptHelper(result, oc);
            } else {
                oc(null, null, null);
                return null;
            }
        }

        private IEnumerator<object> OnEventEnter (EventType type, object evt) {
            IEntity sender = Event.GetProp<IEntity>("Sender", evt);

            if (sender == this) {
                _LastPrompt = false;
                Client.ClearScreen();
                SendMessage("You enter {0}.", Location.Title ?? Location.Name);
                PerformLook();
            } else {
                SendMessage("{0} enters the room.", sender);
            }

            return null;
        }

        private IEnumerator<object> OnEventLeave (EventType type, object evt) {
            IEntity sender = Event.GetProp<IEntity>("Sender", evt);

            if (sender != this)
                SendMessage("{0} leaves the room.", sender);

            return null;
        }

        private IEnumerator<object> OnEventSay (EventType type, object evt) {
            IEntity sender = Event.GetProp<IEntity>("Sender", evt);
            string text = Event.GetProp<string>("Text", evt);

            if (sender == this) {
                SendMessage("You say, \"{0}\"", text);
            } else {
                SendMessage("{0} says, \"{1}\"", sender, text);
            }

            return null;
        }

        private IEnumerator<object> OnEventEmote (EventType type, object evt) {
            SendMessage(Event.Format("{Sender} {Text}", this, evt));
            return null;
        }

        private IEnumerator<object> OnEventDeath (EventType type, object evt) {
            IEntity sender = Event.GetProp<IEntity>("Sender", evt);

            if (sender == this) {
                SendMessage("You collapse onto the floor and release your last breath.");
            } else {
                SendMessage("{0} collapses onto the floor, releasing their last breath!", sender);
            }

            return null;
        }

        private IEnumerator<object> OnEventTell (EventType type, object evt) {
            IEntity sender = Event.GetProp<IEntity>("Sender", evt);
            IEntity recipient = Event.GetProp<IEntity>("Recipient", evt);
            string text = Event.GetProp<string>("Text", evt);

            if (sender == this) {
                SendMessage("You tell {0}, \"{1}\"", recipient, text);
            } else {
                SendMessage("{0} tells you, \"{1}\"", sender, text);
            }

            return null;
        }

        private IEnumerator<object> OnEventCombatStart (EventType type, object evt) {
            IEntity sender = Event.GetProp<IEntity>("Sender", evt);
            IEntity target = Event.GetProp<IEntity>("Target", evt);

            if (sender == this) {
                SendMessage("You lunge at {0} and attack!", target);
            } else {
                SendMessage(Event.Format("{Sender} charges at {Target} and attacks!", this, evt));
            }

            return null;
        }

        private IEnumerator<object> OnEventCombatHit (EventType type, object evt) {
            IEntity sender = Event.GetProp<IEntity>("Sender", evt);

            if (sender == this) {
                SendMessage(Event.Format("You hit {Target} with your {WeaponName} for {Damage} damage.", this, evt));
            } else {
                SendMessage(Event.Format("{Sender} hits {Target} with their {WeaponName} for {Damage} damage.", this, evt));
            }

            return null;
        }

        private IEnumerator<object> OnEventCombatMiss (EventType type, object evt) {
            IEntity sender = Event.GetProp<IEntity>("Sender", evt);
            IEntity target = Event.GetProp<IEntity>("Target", evt);
            string weaponName = Event.GetProp<string>("WeaponName", evt);

            if (sender == this) {
                SendMessage("You miss {0} with your {1}.", target, weaponName);
            } else {
                SendMessage(Event.Format("{Sender} misses {Target} with their {WeaponName}.", this, evt));
            }

            return null;
        }

        protected override IEnumerator<object> ThinkTask () {
            while (Name == null) {
                Client.SendText("Greetings, traveller. What might your name be?\r\n");
                Future f = Client.ReadLineText();
                yield return f;
                string tempName;
                try {
                    tempName = (f.Result as string).Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[0];
                } catch {
                    continue;
                }
                if (World.Players.ContainsKey(tempName.ToLower())) {
                    Client.SendText("A player with that name is already logged in.\r\n");
                    continue;
                }
                Name = tempName;
            }

            Console.WriteLine("{0} has entered the world", Name);
            World.Players[Name.ToLower()] = this;

            while (true) {
                Future newInputLine = Client.ReadLineText();
                yield return newInputLine;
                string line = newInputLine.Result as string;

                if (line != null) {
                    yield return ProcessInput(line);
                }
            }
        }
    }
}

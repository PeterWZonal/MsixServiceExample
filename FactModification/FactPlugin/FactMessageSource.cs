using BackgroundService.Contracts;
using System;

namespace FactPlugin
{
    public sealed class FactMessageSource : IPeriodicMessageSource
    {
        public string Name => "FactPlugin";

        public TimeSpan Interval => TimeSpan.FromSeconds(45);

        public string GetMessage() => _facts[Random.Shared.Next(_facts.Length)];

        private readonly string[] _facts =
        {
            "Light from the Sun takes a little over eight minutes to reach Earth.",
            "A day on Venus is longer than its year.",
            "Octopuses have three hearts and blue, copper-based blood.",
            "The first computer bug was a real moth found in a Harvard relay in 1947.",
            "Honey never spoils; edible honey has been found in ancient Egyptian tombs.",
            "There are more possible chess games than atoms in the observable universe.",
            "Bananas are mildly radioactive because of their potassium-40 content.",
            "The Eiffel Tower grows several centimetres taller in summer as the iron expands.",
            "A teaspoon of neutron star material would weigh billions of tonnes.",
            "Water expands by about nine percent when it freezes.",
            "The ENIAC computer weighed about 27 tonnes and filled a large room.",
            "Sharks have existed for longer than trees have.",
            "Sound travels roughly four times faster in water than in air.",
            "The Moon drifts about 3.8 centimetres further from Earth every year.",
            "A single lightning bolt can heat the air around it to five times the temperature of the Sun's surface."
        };
    }
}

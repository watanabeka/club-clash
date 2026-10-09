using UnityEngine;

namespace ClubClash
{
    // Idle anatomical landmarks, audited against the baseball source. Hair ornaments,
    // weapons and bent/spread legs do not set scale. One scale is shared by all poses.
    public static class BodyArtProfile
    {
        public static float Scale(string id, float fallbackStature)
        {
            float head, crownToHip;
            switch (id)
            {
                case "home": head=55; crownToHip=142; break;
                case "kendo": head=53; crownToHip=125; break;
                case "soccer": head=52; crownToHip=117; break;
                case "baseball": head=53; crownToHip=116; break;
                case "volleyball": head=62; crownToHip=140; break;
                case "tennis": head=54; crownToHip=130; break;
                case "golf": head=62; crownToHip=137; break;
                case "boxing": head=61; crownToHip=144; break;
                case "archery": head=52; crownToHip=129; break;
                case "music": head=57; crownToHip=136; break;
                case "art": head=59; crownToHip=150; break;
                case "science": head=52; crownToHip=118; break;
                case "shogi": head=59; crownToHip=143; break;
                case "handball": head=60; crownToHip=150; break;
                case "swimming": head=60; crownToHip=145; break;
                case "basketball": head=64; crownToHip=154; break;
                case "badminton": head=58; crownToHip=135; break;
                case "judo": head=60; crownToHip=145; break;
                case "calligraphy": head=57; crownToHip=147; break;
                default: return 180f/Mathf.Max(1,fallbackStature);
            }
            float adjustment=id=="soccer"?.86f:id=="tennis"?.9f:id=="badminton"?.9f:id=="volleyball"?.94f:1f;
            return adjustment * 180f/187f * Mathf.Sqrt((53f/head)*(116f/crownToHip));
        }
    }
}

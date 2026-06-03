using System;
using System.Collections;
using System.Collections.Generic;

namespace MusicBeePlugin
{
    public class AiResponseParser
    {
        public AiChatResponse Parse(string raw)
        {
            IDictionary<string, object> root = SimpleJson.Parse(ExtractJson(raw)) as IDictionary<string, object>;
            if (root == null)
            {
                throw new FormatException("Model response is not a JSON object.");
            }

            AiChatResponse response = new AiChatResponse();
            response.Message = SimpleJson.GetString(root, "message");

            object actionsValue;
            IList actions = root.TryGetValue("actions", out actionsValue) ? actionsValue as IList : null;
            if (actions != null)
            {
                foreach (object item in actions)
                {
                    IDictionary<string, object> actionObject = item as IDictionary<string, object>;
                    if (actionObject == null)
                    {
                        continue;
                    }

                    AiAction action = new AiAction();
                    action.Type = SimpleJson.GetString(actionObject, "type");
                    action.RequiresConfirmation = SimpleJson.GetBool(actionObject, "requiresConfirmation", true);
                    action.Title = SimpleJson.GetString(actionObject, "title");
                    action.Explanation = SimpleJson.GetString(actionObject, "explanation");

                    object idsValue;
                    IList ids = actionObject.TryGetValue("trackIds", out idsValue) ? idsValue as IList : null;
                    if (ids != null)
                    {
                        foreach (object id in ids)
                        {
                            action.TrackIds.Add(Convert.ToString(id));
                        }
                    }

                    response.Actions.Add(action);
                }
            }

            object toolRequestsValue;
            IList toolRequests = root.TryGetValue("toolRequests", out toolRequestsValue) ? toolRequestsValue as IList : null;
            if (toolRequests != null)
            {
                foreach (object item in toolRequests)
                {
                    IDictionary<string, object> toolObject = item as IDictionary<string, object>;
                    if (toolObject == null)
                    {
                        continue;
                    }

                    ToolRequest request = new ToolRequest();
                    request.Name = SimpleJson.GetString(toolObject, "name");
                    request.Query = SimpleJson.GetString(toolObject, "query");
                    int limit;
                    if (int.TryParse(SimpleJson.GetString(toolObject, "limit"), out limit))
                    {
                        request.Limit = limit;
                    }
                    response.ToolRequests.Add(request);
                }
            }

            return response;
        }

        public static string ExtractJson(string raw)
        {
            string text = (raw ?? "").Trim();
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                return text.Substring(start, end - start + 1);
            }
            return text;
        }
    }
}

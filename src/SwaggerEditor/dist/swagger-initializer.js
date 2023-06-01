window.onload = function() {
  //<editor-fold desc="Changeable Configuration Block">

    const uri = (new URLSearchParams(window.location.search)).get("uri");

    let swaggerUrl = "https://petstore.swagger.io/v2/swagger.json"; // default.

    if (uri.length > 0) {
        swaggerUrl = uri;
        console.log("has Uri property: " + uri);
    }

  // the following lines will be replaced by docker/configurator, when it runs in a docker-container
  window.ui = SwaggerUIBundle({
    url: swaggerUrl,
    dom_id: '#swagger-ui',
    deepLinking: true,
    presets: [
      SwaggerUIBundle.presets.apis,
      SwaggerUIStandalonePreset
    ],
    plugins: [
      SwaggerUIBundle.plugins.DownloadUrl
    ],
    layout: "StandaloneLayout"
  });

  //</editor-fold>
};

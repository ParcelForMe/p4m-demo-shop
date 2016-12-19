/// <binding BeforeBuild='default' />
/*
This file in the main entry point for defining Gulp tasks and using Gulp plugins.
Click here to learn more. http://go.microsoft.com/fwlink/?LinkId=518007
*/

var gulp = require('gulp');
var del = require('del');
var gutil = require('gulp-util');
var vulcanize = require('gulp-vulcanize');
var runSeries = require('run-sequence');
var concat = require('gulp-concat');
var htmlmin = require('gulp-htmlmin');
var pump = require('pump');

gulp.task('default', function () {
    runSeries('clean', 'copyBowerComponents');
});

gulp.task('copyBowerComponents', function () {
    gutil.log("Copying ./bower_components/**/* to ./Scripts/Lib");
    return gulp.src("./bower_components/**/*").pipe(gulp.dest("./Scripts/Lib"));
});

gulp.task('clean', function () {
    // We clear the Scripts/Lib to ensure that the Scripts/Lib directory does not 
    // include any files that have since been removed from referenced Bower packages
    gutil.log("Cleaning ./Scripts/Lib/**/*");
    return del([
        './Scripts/Lib/**/*'
    ]);
});

// Example gulp-vulcanize task to vulcanize the p4m-widgets
gulp.task('vulcanize', function (cb) {
    // Vulcanize the p4m-widget polymer
    pump(
    [
        gulp.src('./Scripts/p4m-widget.html'),
        vulcanize({
            stripComments: true,
            inlineScripts: true,
            inlineCss: false
        }),
        htmlmin({
            //removeEmptyAttributes: true,
            //customAttrAssign: [{ "source": "\\$=" }],
            //customAttrSurround: [
            //    [{ "source": "\\({\\{" }, { "source": "\\}\\}" }],
            //    [{ "source": "\\[\\[" }, { "source": "\\]\\]" }]
            //],
            collapseWhitespace: true,
            // always leave one space
            // because http://perfectionkills.com/experimenting-with-html-minifier/#collapse_whitespace
            conservativeCollapse: true,
            minifyJS: true,
            minifyCSS: true,
            removeComments: true,
            removeCommentsFromCDATA: true,
            removeCDATASectionsFromCDATA: true
        }),
        concat('p4m-widget.vulcanized.html'),
        gulp.dest('Scripts')
    ],
    cb);
});